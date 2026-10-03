using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Persistence;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-08: <see cref="EfDecisionAuditStore"/> dịch lỗi PostgreSQL đúng hợp đồng của <see cref="IDecisionAuditStore"/> mà không
/// cần cơ sở dữ liệu thật — <c>40001</c>/<c>40P01</c> thành <see cref="PersistenceUnavailableException"/> (giữ exception gốc,
/// thông báo không chứa dữ liệu), vi phạm ràng buộc khác để nguyên exception gốc, và hủy của caller lan ra nguyên vẹn.
/// Lỗi được gài qua <see cref="TadssDbContext"/> giả ghi đè <c>SaveChangesAsync</c>; không mở kết nối nào.
/// </summary>
public class EfDecisionAuditStoreErrorTests
{
    private const string Secret = "Host=secret-host;Password=hunter2";

    private static DbContextOptions<TadssDbContext> FakeOptions() =>
        new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Timeout=1")
            .Options;

    private static PostgresException Postgres(string sqlState, string? constraintName = null) =>
        new(
            messageText: $"loi gia lap {Secret}",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            constraintName: constraintName);

    private static DecisionExecutionRecord Record() =>
        new(
            DecisionId: Guid.NewGuid(),
            ReservationId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Role: ExecutionRole.PRIMARY,
            StrategyName: "DefaultDecisionStrategy",
            StrategyVersion: "2.0.0",
            StrategyStatus: DecisionStatus.REJECTED,
            Status: DecisionStatus.REJECTED,
            DepositRate: null,
            DepositAmount: null,
            ReasonCode: "RULE_REJECTED",
            ExecutionType: ExecutionType.AUTOMATED,
            ExecutionTimeMs: 3,
            FallbackUsed: false,
            FailureReason: null,
            CacheHit: false,
            ApprovalExpiresAt: null,
            RequestHash: new string('b', 64),
            RequestPayload: "{}",
            ResponsePayload: "{}",
            IdempotencyKey: "key-1",
            CorrelationId: null,
            CreatedAt: new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero));

    private static EfDecisionAuditStore StoreOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionAuditStore>.Instance);

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [Trait("CatalogId", "UT-08")]
    public async Task Insert_SerializationFailureOrDeadlock_ThrowsPersistenceUnavailable_Theory(string sqlState)
    {
        var original = Postgres(sqlState);
        await using var db = new FailingSaveDbContext(FakeOptions(), new DbUpdateException("save failed", original));
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(Record(), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<PersistenceUnavailableException>()).Which;
        thrown.InnerException.Should().NotBeNull();
        PersistenceFailureDetector.IsRetryable(thrown.InnerException!).Should().BeTrue("exception gốc phải được giữ để chẩn đoán");
        thrown.Message.Should().NotContain(Secret).And.NotContain("INSERT").And.NotContain("decision_executions");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Insert_TransientNpgsqlException_ThrowsPersistenceUnavailableKeepingInner()
    {
        var original = new NpgsqlException("mat ket noi", new TimeoutException());
        await using var db = new FailingSaveDbContext(FakeOptions(), new DbUpdateException("save failed", original));
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(Record(), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<PersistenceUnavailableException>()).Which;
        thrown.InnerException.Should().BeOfType<DbUpdateException>().Which.InnerException.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData("23505", "uq_decision_executions_decision_id")]
    [InlineData("23505", null)]
    [InlineData("23514", "ck_de_deposit")]
    [InlineData("22001", null)]
    [InlineData("23503", "fk_do_decision_id")]
    [Trait("CatalogId", "UT-08")]
    public async Task Insert_NonRetryableConstraintOrDataError_PropagatesOriginalException_Theory(string sqlState, string? constraint)
    {
        var original = new DbUpdateException("save failed", Postgres(sqlState, constraint));
        await using var db = new FailingSaveDbContext(FakeOptions(), original);
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(Record(), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.Should().BeSameAs(original, "chỉ lỗi có thể thử lại mới bị bọc; vi phạm ràng buộc phải thành 500");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Insert_FailedSave_DetachesEntityFromChangeTracker()
    {
        await using var db = new FailingSaveDbContext(FakeOptions(), new DbUpdateException("save failed", Postgres("23514", "ck_de_deposit")));
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(Record(), CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Entries().Should().BeEmpty("entity thất bại không được nằm lại trong DbContext của scope");
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Store_CancelledToken_PropagatesOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var insertDb = new TadssDbContext(FakeOptions());
        var insert = () => StoreOver(insertDb).InsertExecutionAsync(Record(), cts.Token);
        await insert.Should().ThrowAsync<OperationCanceledException>();

        await using var findDb = new TadssDbContext(FakeOptions());
        var find = () => StoreOver(findDb).FindPrimaryByIdempotencyKeyAsync("key-1", cts.Token);
        await find.Should().ThrowAsync<OperationCanceledException>();

        await using var detailDb = new TadssDbContext(FakeOptions());
        var detail = () => StoreOver(detailDb).GetDecisionDetailAsync(Guid.NewGuid(), cts.Token);
        await detail.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Insert_CancelledCallerWithTimeoutLikeFailure_StillPropagatesCancellationNotPersistenceUnavailable()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await using var db = new FailingSaveDbContext(FakeOptions(), new OperationCanceledException(cts.Token));
        var store = StoreOver(db);

        var act = () => store.InsertExecutionAsync(Record(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// <see cref="TadssDbContext"/> giả: <c>SaveChangesAsync</c> luôn ném exception định sẵn (không mở kết nối).
    /// </summary>
    private sealed class FailingSaveDbContext : TadssDbContext
    {
        private readonly Exception _failure;

        public FailingSaveDbContext(DbContextOptions<TadssDbContext> options, Exception failure) : base(options)
        {
            _failure = failure;
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<int>(_failure);
    }
}
