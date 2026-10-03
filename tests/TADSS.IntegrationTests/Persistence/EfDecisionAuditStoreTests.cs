using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.IntegrationTests.Migrations;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Middlewares;
using Xunit;

namespace TADSS.IntegrationTests.Persistence;

/// <summary>
/// IT-01/IT-04/IT-05/FI-08: <see cref="EfDecisionAuditStore"/> trên PostgreSQL thật (Testcontainers) — insert một hàng đúng bằng record,
/// tranh chấp idempotency trả bản thắng (kể cả 50 request song song), vi phạm ràng buộc khác giữ nguyên exception gốc,
/// mất kết nối (Toxiproxy) thành <see cref="PersistenceUnavailableException"/>, và đọc chi tiết quyết định kèm override/outcome.
/// Cột <c>jsonb</c> do PostgreSQL chuẩn hóa văn bản (khoảng trắng, thứ tự khóa) nên payload đọc lại được so sánh theo nội dung JSON.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class EfDecisionAuditStoreTests : IntegrationTestBase
{
    private const string IdempotencyConstraint = "uq_decision_idempotency_key";
    private const string DecisionIdConstraint = "uq_decision_executions_decision_id";
    private static readonly DateTimeOffset EvaluatedAt = new DateTimeOffset(2026, 10, 2, 8, 30, 15, TimeSpan.Zero).AddTicks(1234560);

    public EfDecisionAuditStoreTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    public override async Task DisposeAsync()
    {
        // Luôn khôi phục proxy để không ảnh hưởng test khác trong collection
        try { await Infrastructure.Toxiproxy.ResetAsync(); }
        finally { await base.DisposeAsync(); }
    }

    private static EfDecisionAuditStore StoreOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionAuditStore>.Instance);

    private static DecisionExecutionRecord NewRecord(
        string? idempotencyKey = null,
        Guid? decisionId = null,
        ExecutionRole role = ExecutionRole.PRIMARY,
        DecisionStatus status = DecisionStatus.APPROVED) =>
        new(
            DecisionId: decisionId ?? Guid.NewGuid(),
            ReservationId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Role: role,
            StrategyName: "DefaultDecisionStrategy",
            StrategyVersion: "2.0.0",
            StrategyStatus: status,
            Status: status,
            DepositRate: status == DecisionStatus.APPROVED ? 0.3m : null,
            DepositAmount: status == DecisionStatus.APPROVED ? 90000.50m : null,
            ReasonCode: "RULE_APPROVED",
            ExecutionType: ExecutionType.AUTOMATED,
            ExecutionTimeMs: 17,
            FallbackUsed: false,
            FailureReason: null,
            CacheHit: false,
            ApprovalExpiresAt: status == DecisionStatus.NEED_APPROVAL ? EvaluatedAt.AddHours(2) : null,
            RequestHash: new string('c', 64),
            RequestPayload: "{\"reservationId\":\"abc\",\"items\":[1,2,3],\"note\":\"Đặt sân cầu lông\"}",
            ResponsePayload: "{\"status\":\"" + status + "\",\"rate\":0.30}",
            IdempotencyKey: idempotencyKey,
            CorrelationId: "corr-" + Guid.NewGuid().ToString("N"),
            CreatedAt: EvaluatedAt);

    /// <summary>So sánh hai bản ghi: mọi trường bằng nhau, riêng payload jsonb so theo nội dung JSON (PostgreSQL chuẩn hóa văn bản).</summary>
    private static void AssertSameRecord(DecisionExecutionRecord actual, DecisionExecutionRecord expected)
    {
        actual.Should().BeEquivalentTo(
            expected,
            options => options
                .ComparingByMembers<DecisionExecutionRecord>()
                .Excluding(record => record.RequestPayload)
                .Excluding(record => record.ResponsePayload));
        JsonNode.DeepEquals(JsonNode.Parse(actual.RequestPayload), JsonNode.Parse(expected.RequestPayload)).Should().BeTrue("request_payload giữ nguyên nội dung JSON");
        JsonNode.DeepEquals(JsonNode.Parse(actual.ResponsePayload), JsonNode.Parse(expected.ResponsePayload)).Should().BeTrue("response_payload giữ nguyên nội dung JSON");
    }

    private Task<int> CountRowsAsync(string whereClause = "true") =>
        AuditSchemaSql.ScalarAsync<int>(ConnectionString, $"SELECT count(*) FROM {AuditSchemaSql.Executions} WHERE {whereClause}");

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task Insert_NewExecution_StoresExactlyOneRowEqualToRecord()
    {
        var record = NewRecord(idempotencyKey: "it01-" + Guid.NewGuid());
        await using var db = CreateDbContext();

        var result = await StoreOver(db).InsertExecutionAsync(record, CancellationToken.None);

        result.IsExistingWinner.Should().BeFalse();
        result.Stored.Should().Be(record);
        db.ChangeTracker.Entries().Should().BeEmpty("store không giữ entity đã ghi trong change tracker");
        (await CountRowsAsync()).Should().Be(1);
        await using var readDb = CreateDbContext();
        var stored = await StoreOver(readDb).FindPrimaryByIdempotencyKeyAsync(record.IdempotencyKey!, CancellationToken.None);
        stored.Should().NotBeNull();
        AssertSameRecord(stored!, record);
    }

    [Theory]
    [InlineData(DecisionStatus.APPROVED)]
    [InlineData(DecisionStatus.NEED_APPROVAL)]
    [InlineData(DecisionStatus.REJECTED)]
    [Trait("CatalogId", "IT-01")]
    public async Task Insert_EachStatus_RoundTripsThroughDatabase_Theory(DecisionStatus status)
    {
        var record = NewRecord(idempotencyKey: "it01-status-" + status, status: status) with { CorrelationId = null };
        await using var db = CreateDbContext();
        await StoreOver(db).InsertExecutionAsync(record, CancellationToken.None);

        await using var readDb = CreateDbContext();
        var detail = await StoreOver(readDb).GetDecisionDetailAsync(record.DecisionId, CancellationToken.None);

        detail.Should().NotBeNull();
        AssertSameRecord(detail!.Execution, record);
    }

    [Fact]
    [Trait("CatalogId", "IT-04")]
    public async Task Insert_DuplicateIdempotencyKey_ReturnsExistingWinner()
    {
        var key = "it04-" + Guid.NewGuid();
        var first = NewRecord(idempotencyKey: key);
        var second = NewRecord(idempotencyKey: key);
        await using var db = CreateDbContext();
        var store = StoreOver(db);
        await store.InsertExecutionAsync(first, CancellationToken.None);

        var result = await store.InsertExecutionAsync(second, CancellationToken.None);

        result.IsExistingWinner.Should().BeTrue();
        AssertSameRecord(result.Stored, first);
        (await CountRowsAsync()).Should().Be(1);
        db.ChangeTracker.Entries().Should().BeEmpty("không entity nào được giữ lại trong change tracker");
    }

    [Fact]
    [Trait("CatalogId", "IT-04")]
    public async Task Insert_FiftyConcurrentSameKey_OneRowAllReturnWinner()
    {
        const int concurrency = 50;
        var key = "it04-concurrent-" + Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            // Mỗi task có scope/DbContext và store riêng, như mỗi request thật
            await using var db = CreateDbContext();
            var store = StoreOver(db);
            var record = NewRecord(idempotencyKey: key);
            await start.Task;
            return (Record: record, Result: await store.InsertExecutionAsync(record, CancellationToken.None));
        })).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        (await CountRowsAsync($"idempotency_key = '{key}'")).Should().Be(1);
        var winners = outcomes.Where(outcome => !outcome.Result.IsExistingWinner).ToList();
        winners.Should().ContainSingle("đúng một request ghi được hàng");
        outcomes.Count(outcome => outcome.Result.IsExistingWinner).Should().Be(concurrency - 1);
        var winnerRecord = winners[0].Record;
        outcomes.Select(outcome => outcome.Result.Stored.DecisionId).Distinct().Should().Equal(winnerRecord.DecisionId);
        foreach (var outcome in outcomes)
        {
            AssertSameRecord(outcome.Result.Stored, winnerRecord);
        }
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task Insert_DuplicateDecisionIdDifferentKey_PropagatesPostgresException()
    {
        var decisionId = Guid.NewGuid();
        await using var db = CreateDbContext();
        var store = StoreOver(db);
        await store.InsertExecutionAsync(NewRecord(idempotencyKey: "it05-a-" + Guid.NewGuid(), decisionId: decisionId), CancellationToken.None);

        var act = () => store.InsertExecutionAsync(NewRecord(idempotencyKey: "it05-b-" + Guid.NewGuid(), decisionId: decisionId), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        var postgres = thrown.InnerException.Should().BeOfType<PostgresException>().Which;
        postgres.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.Should().Be(DecisionIdConstraint).And.NotBe(IdempotencyConstraint);
        (await CountRowsAsync()).Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "IT-05")]
    public async Task Insert_CheckViolation_PropagatesOriginalException()
    {
        // APPROVED mà thiếu mức cọc: vi phạm ck_de_deposit
        var invalid = NewRecord(idempotencyKey: "it05-ck-" + Guid.NewGuid()) with { DepositRate = null, DepositAmount = null };
        await using var db = CreateDbContext();
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(invalid, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        var postgres = thrown.InnerException.Should().BeOfType<PostgresException>().Which;
        postgres.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        postgres.ConstraintName.Should().Be("ck_de_deposit");
        (await CountRowsAsync()).Should().Be(0);

        // DbContext của scope vẫn dùng được sau lỗi: entity thất bại đã được gỡ khỏi change tracker
        var recovered = await store.InsertExecutionAsync(NewRecord(idempotencyKey: "it05-ck-ok-" + Guid.NewGuid()), CancellationToken.None);
        recovered.IsExistingWinner.Should().BeFalse();
        (await CountRowsAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("find")]
    [InlineData("detail")]
    [Trait("CatalogId", "FI-08")]
    public async Task Insert_ConnectionLost_ThrowsPersistenceUnavailable(string operation)
    {
        // Store đi qua Toxiproxy; chạy thử một thao tác khỏe rồi cắt kết nối PostgreSQL
        await using var db = CreateDbContext(Infrastructure.BuildProxiedConnectionString(DatabaseName));
        var store = StoreOver(db);
        (await store.FindPrimaryByIdempotencyKeyAsync("fi08-healthy", CancellationToken.None)).Should().BeNull("đường qua Toxiproxy phải chạy trước khi tiêm lỗi");
        await Infrastructure.Toxiproxy.SetEnabledAsync(TadssInfrastructureFixture.PostgresProxy, enabled: false);

        Func<Task> act = operation switch
        {
            "insert" => () => store.InsertExecutionAsync(NewRecord(idempotencyKey: "fi08-down"), CancellationToken.None),
            "find" => () => store.FindPrimaryByIdempotencyKeyAsync("fi08-down", CancellationToken.None),
            _ => () => store.GetDecisionDetailAsync(Guid.NewGuid(), CancellationToken.None),
        };

        var thrown = (await act.Should().ThrowAsync<PersistenceUnavailableException>()).Which;
        thrown.InnerException.Should().NotBeNull("exception gốc của driver phải được giữ");
        PersistenceFailureDetector.IsRetryable(thrown.InnerException!).Should().BeTrue();

        await Infrastructure.Toxiproxy.ResetAsync();
        (await CountRowsAsync()).Should().Be(0, "không để lại bản ghi dở dang");
    }

    [Fact]
    [Trait("CatalogId", "IT-04")]
    public async Task FindPrimary_ShadowRecordWithSameKey_Ignored()
    {
        var key = "it04-shadow-" + Guid.NewGuid();
        await using var db = CreateDbContext();
        var store = StoreOver(db);
        await store.InsertExecutionAsync(NewRecord(idempotencyKey: key, role: ExecutionRole.SHADOW), CancellationToken.None);

        var found = await store.FindPrimaryByIdempotencyKeyAsync(key, CancellationToken.None);

        found.Should().BeNull("idempotency chỉ áp cho bản PRIMARY");
        (await CountRowsAsync($"idempotency_key = '{key}'")).Should().Be(1);
    }

    [Fact]
    [Trait("CatalogId", "IT-04")]
    public async Task FindPrimary_UnknownKey_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var found = await StoreOver(db).FindPrimaryByIdempotencyKeyAsync("khong-ton-tai", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDetail_WithOverrideAndOutcomeRows_ReturnsJoined()
    {
        var record = NewRecord(idempotencyKey: "it01-detail-" + Guid.NewGuid(), status: DecisionStatus.NEED_APPROVAL);
        await using (var writeDb = CreateDbContext())
        {
            await StoreOver(writeDb).InsertExecutionAsync(record, CancellationToken.None);
        }

        var overrideRow = AuditSchemaSql.ValidOverride(record.DecisionId);
        var outcomeRow = AuditSchemaSql.ValidOutcome(record.DecisionId);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Overrides, overrideRow);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Outcomes, outcomeRow);

        await using var db = CreateDbContext();
        var detail = await StoreOver(db).GetDecisionDetailAsync(record.DecisionId, CancellationToken.None);

        detail.Should().NotBeNull();
        AssertSameRecord(detail!.Execution, record);
        detail.Override.Should().Be(new DecisionOverrideRecord(
            record.DecisionId,
            OverrideAction.APPROVE,
            (decimal)overrideRow["deposit_rate"]!,
            (decimal)overrideRow["deposit_amount"]!,
            (string)overrideRow["actor_id"]!,
            (string)overrideRow["reason"]!,
            (string)overrideRow["idempotency_key"]!,
            (DateTimeOffset)overrideRow["created_at"]!));
        detail.Outcome.Should().Be(new DecisionOutcomeRecord(
            record.DecisionId,
            Shared.DecisionContractSDK.Enums.DecisionOutcome.COMPLETED,
            (DateTimeOffset)outcomeRow["occurred_at"]!,
            (DateTimeOffset)outcomeRow["reported_at"]!));
        db.ChangeTracker.Entries().Should().BeEmpty("truy vấn đọc phải là AsNoTracking");
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDetail_ExecutionWithoutOverrideOrOutcome_ReturnsExecutionOnly()
    {
        var record = NewRecord(idempotencyKey: "it01-bare-" + Guid.NewGuid());
        await using var db = CreateDbContext();
        var store = StoreOver(db);
        await store.InsertExecutionAsync(record, CancellationToken.None);

        var detail = await store.GetDecisionDetailAsync(record.DecisionId, CancellationToken.None);

        detail.Should().NotBeNull();
        AssertSameRecord(detail!.Execution, record);
        detail.Override.Should().BeNull();
        detail.Outcome.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDetail_ShadowExecution_ReturnsRecordWithoutRoleFilter()
    {
        var record = NewRecord(idempotencyKey: "it01-shadow-" + Guid.NewGuid(), role: ExecutionRole.SHADOW);
        await using var db = CreateDbContext();
        var store = StoreOver(db);
        await store.InsertExecutionAsync(record, CancellationToken.None);

        var detail = await store.GetDecisionDetailAsync(record.DecisionId, CancellationToken.None);

        detail.Should().NotBeNull("chi tiết tra theo decisionId, không lọc theo role");
        detail!.Execution.Role.Should().Be(ExecutionRole.SHADOW);
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDetail_Unknown_ReturnsNull()
    {
        await using var db = CreateDbContext();

        var detail = await StoreOver(db).GetDecisionDetailAsync(Guid.NewGuid(), CancellationToken.None);

        detail.Should().BeNull();
    }
}
