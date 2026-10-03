using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.IntegrationTests.Migrations;
using Xunit;

namespace TADSS.IntegrationTests.Persistence;

/// <summary>
/// IT-06/IT-07/IT-10/UT-08: <see cref="EfDecisionGovernanceStore"/> trên PostgreSQL thật (Testcontainers) — ghi override và outcome,
/// tranh chấp <c>UNIQUE(decision_id)</c> trả bản đã có (kể cả 20 scope song song), vi phạm ràng buộc khác (khóa ngoại, CHECK)
/// giữ nguyên exception gốc thay vì thành <see cref="PersistenceUnavailableException"/>.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class GovernanceStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    public GovernanceStoreTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    private static EfDecisionGovernanceStore GovernanceOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionGovernanceStore>.Instance);

    private static EfDecisionAuditStore AuditOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionAuditStore>.Instance);

    private static DecisionOverrideRecord NewOverride(Guid decisionId, string? key = null, string reason = "Khách quen, duyệt cọc 50%") =>
        new(
            DecisionId: decisionId,
            Action: OverrideAction.APPROVE,
            DepositRate: 0.5m,
            DepositAmount: 150000.00m,
            ActorId: "manager-01",
            Reason: reason,
            IdempotencyKey: key ?? "ov-" + Guid.NewGuid().ToString("N"),
            CreatedAt: BaseTime.AddTicks(1230));

    private static DecisionOutcomeRecord NewOutcome(Guid decisionId, DecisionOutcome outcome = DecisionOutcome.COMPLETED) =>
        new(decisionId, outcome, BaseTime.AddHours(-1).AddTicks(450), BaseTime.AddTicks(670));

    /// <summary>Gài một quyết định <c>NEED_APPROVAL</c> bằng SQL trực tiếp rồi trả <c>decision_id</c>.</summary>
    private async Task<Guid> SeedPendingDecisionAsync()
    {
        var decisionId = Guid.NewGuid();
        var row = AuditSchemaSql.ValidExecution(decisionId);
        row["status"] = "NEED_APPROVAL";
        row["strategy_status"] = "NEED_APPROVAL";
        row["deposit_rate"] = null;
        row["deposit_amount"] = null;
        row["approval_expires_at"] = BaseTime.AddHours(2);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Executions, row);
        return decisionId;
    }

    private Task<int> CountAsync(string table, Guid decisionId) =>
        AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {table} WHERE decision_id = '{decisionId}'");

    [Fact]
    [Trait("CatalogId", "IT-06")]
    public async Task InsertOverride_ThenDetail_ReturnsOverride()
    {
        var decisionId = await SeedPendingDecisionAsync();
        var record = NewOverride(decisionId);
        await using var db = CreateDbContext();

        var result = await GovernanceOver(db).InsertOverrideAsync(record, CancellationToken.None);

        result.IsExistingWinner.Should().BeFalse();
        result.Stored.Should().Be(record);
        db.ChangeTracker.Entries().Should().BeEmpty("store không giữ entity đã ghi trong change tracker");
        (await CountAsync(AuditSchemaSql.Overrides, decisionId)).Should().Be(1);
        await using var readDb = CreateDbContext();
        var detail = await AuditOver(readDb).GetDecisionDetailAsync(decisionId, CancellationToken.None);
        detail.Should().NotBeNull();
        detail!.Override.Should().Be(record, "đọc lại đúng từng cột (kể cả µs của created_at)");
        detail.Outcome.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "IT-06")]
    public async Task InsertOverride_Reject_StoresWithoutDeposit()
    {
        var decisionId = await SeedPendingDecisionAsync();
        var record = NewOverride(decisionId) with { Action = OverrideAction.REJECT, DepositRate = null, DepositAmount = null };
        await using var db = CreateDbContext();

        var result = await GovernanceOver(db).InsertOverrideAsync(record, CancellationToken.None);

        result.IsExistingWinner.Should().BeFalse();
        await using var readDb = CreateDbContext();
        (await AuditOver(readDb).GetDecisionDetailAsync(decisionId, CancellationToken.None))!.Override.Should().Be(record);
    }

    [Fact]
    [Trait("CatalogId", "IT-07")]
    public async Task InsertOverride_Duplicate_ReturnsExistingWinner()
    {
        var decisionId = await SeedPendingDecisionAsync();
        var first = NewOverride(decisionId, reason: "Lý do của bản thắng");
        var second = NewOverride(decisionId, reason: "Lý do của bản thua") with { ActorId = "manager-02", DepositRate = 0.7m, DepositAmount = 210000m };
        await using var db = CreateDbContext();
        var store = GovernanceOver(db);
        await store.InsertOverrideAsync(first, CancellationToken.None);

        var result = await store.InsertOverrideAsync(second, CancellationToken.None);

        result.IsExistingWinner.Should().BeTrue();
        result.Stored.Should().Be(first, "Stored là bản thắng đã lưu, không phải bản vừa gửi");
        (await CountAsync(AuditSchemaSql.Overrides, decisionId)).Should().Be(1);
        db.ChangeTracker.Entries().Should().BeEmpty("không entity nào được giữ lại trong change tracker");
    }

    [Fact]
    [Trait("CatalogId", "IT-07")]
    public async Task InsertOverride_ConcurrentTwenty_ExactlyOneRow()
    {
        const int concurrency = 20;
        var decisionId = await SeedPendingDecisionAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
        {
            // Mỗi task có scope/DbContext và store riêng, như mỗi request thật
            await using var db = CreateDbContext();
            var store = GovernanceOver(db);
            var record = NewOverride(decisionId, reason: $"Lý do của request số {index}") with { ActorId = $"manager-{index:00}" };
            await start.Task;
            return (Record: record, Result: await store.InsertOverrideAsync(record, CancellationToken.None));
        })).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        (await CountAsync(AuditSchemaSql.Overrides, decisionId)).Should().Be(1);
        var winners = outcomes.Where(outcome => !outcome.Result.IsExistingWinner).ToList();
        winners.Should().ContainSingle("đúng một request ghi được hàng");
        outcomes.Count(outcome => outcome.Result.IsExistingWinner).Should().Be(concurrency - 1);
        outcomes.Select(outcome => outcome.Result.Stored).Distinct().Should().ContainSingle("mọi kết quả cùng trỏ về bản thắng").Which.Should().Be(winners[0].Record);
    }

    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task InsertOutcome_ThenDetail_ReturnsOutcome()
    {
        var decisionId = await SeedPendingDecisionAsync();
        var record = NewOutcome(decisionId);
        await using var db = CreateDbContext();

        var result = await GovernanceOver(db).InsertOutcomeAsync(record, CancellationToken.None);

        result.IsExistingWinner.Should().BeFalse();
        result.Stored.Should().Be(record);
        db.ChangeTracker.Entries().Should().BeEmpty();
        await using var readDb = CreateDbContext();
        (await AuditOver(readDb).GetDecisionDetailAsync(decisionId, CancellationToken.None))!.Outcome.Should().Be(record);
    }

    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task InsertOutcome_Duplicate_ReturnsExistingWinner()
    {
        var decisionId = await SeedPendingDecisionAsync();
        var first = NewOutcome(decisionId, DecisionOutcome.COMPLETED);
        var second = NewOutcome(decisionId, DecisionOutcome.NO_SHOW) with { OccurredAt = BaseTime.AddHours(-2) };
        await using var db = CreateDbContext();
        var store = GovernanceOver(db);
        await store.InsertOutcomeAsync(first, CancellationToken.None);

        var result = await store.InsertOutcomeAsync(second, CancellationToken.None);

        result.IsExistingWinner.Should().BeTrue();
        result.Stored.Should().Be(first);
        (await CountAsync(AuditSchemaSql.Outcomes, decisionId)).Should().Be(1);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task InsertOutcome_ConcurrentTwenty_ExactlyOneRow()
    {
        const int concurrency = 20;
        var decisionId = await SeedPendingDecisionAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrency).Select(index => Task.Run(async () =>
        {
            await using var db = CreateDbContext();
            var store = GovernanceOver(db);
            var record = NewOutcome(decisionId) with { OccurredAt = BaseTime.AddMinutes(-index) };
            await start.Task;
            return (Record: record, Result: await store.InsertOutcomeAsync(record, CancellationToken.None));
        })).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        (await CountAsync(AuditSchemaSql.Outcomes, decisionId)).Should().Be(1);
        var winners = outcomes.Where(outcome => !outcome.Result.IsExistingWinner).ToList();
        winners.Should().ContainSingle();
        outcomes.Count(outcome => outcome.Result.IsExistingWinner).Should().Be(concurrency - 1);
        outcomes.Select(outcome => outcome.Result.Stored).Distinct().Should().ContainSingle().Which.Should().Be(winners[0].Record);
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task InsertOverride_UnknownDecision_PropagatesForeignKeyViolation()
    {
        await using var db = CreateDbContext();
        var store = GovernanceOver(db);

        var act = () => store.InsertOverrideAsync(NewOverride(Guid.NewGuid()), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>("khóa ngoại không retryable nên không thành 503")).Which;
        var postgres = thrown.InnerException.Should().BeOfType<PostgresException>().Which;
        postgres.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        postgres.ConstraintName.Should().Be("fk_decision_overrides_decision_id");
        db.ChangeTracker.Entries().Should().BeEmpty("entity thất bại đã được gỡ khỏi change tracker");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task InsertOutcome_UnknownDecision_PropagatesForeignKeyViolation()
    {
        await using var db = CreateDbContext();
        var store = GovernanceOver(db);

        var act = () => store.InsertOutcomeAsync(NewOutcome(Guid.NewGuid()), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task InsertOverride_CheckViolation_PropagatesAndContextStaysUsable()
    {
        var decisionId = await SeedPendingDecisionAsync();
        // APPROVE mà thiếu mức cọc: vi phạm ck_do_deposit (không phải unique, không được nuốt thành "bản thắng")
        var invalid = NewOverride(decisionId) with { DepositRate = null, DepositAmount = null };
        await using var db = CreateDbContext();
        var store = GovernanceOver(db);

        var act = () => store.InsertOverrideAsync(invalid, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        var postgres = thrown.InnerException.Should().BeOfType<PostgresException>().Which;
        postgres.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        postgres.ConstraintName.Should().Be("ck_do_deposit");
        (await CountAsync(AuditSchemaSql.Overrides, decisionId)).Should().Be(0);

        var recovered = await store.InsertOverrideAsync(NewOverride(decisionId), CancellationToken.None);
        recovered.IsExistingWinner.Should().BeFalse("DbContext của scope vẫn dùng được sau lỗi");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task InsertOverride_ConnectionLost_ThrowsPersistenceUnavailable()
    {
        var decisionId = await SeedPendingDecisionAsync();
        await using var db = CreateDbContext(Infrastructure.BuildProxiedConnectionString(DatabaseName));
        var store = GovernanceOver(db);
        try
        {
            await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);

            var act = () => store.InsertOverrideAsync(NewOverride(decisionId), CancellationToken.None);

            var thrown = (await act.Should().ThrowAsync<PersistenceUnavailableException>()).Which;
            thrown.InnerException.Should().NotBeNull("exception gốc của driver phải được giữ");
        }
        finally
        {
            await Infrastructure.Toxiproxy.ResetAsync();
        }

        (await CountAsync(AuditSchemaSql.Overrides, decisionId)).Should().Be(0);
    }
}
