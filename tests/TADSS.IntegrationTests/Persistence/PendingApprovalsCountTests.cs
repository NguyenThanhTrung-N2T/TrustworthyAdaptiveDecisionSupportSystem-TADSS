using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TADSS.Infrastructure.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.IntegrationTests.Migrations;
using Xunit;

namespace TADSS.IntegrationTests.Persistence;

/// <summary>
/// IT-09 (gauge hàng chờ, gói 5.5): <c>CountPendingApprovalsByBranchAsync</c> trên PostgreSQL thật đếm đúng <b>cùng tập</b> quyết định chờ duyệt với
/// <c>GetPendingApprovalsAsync</c> (bản <c>PRIMARY</c> mới nhất mỗi đơn, <c>NEED_APPROVAL</c>, chưa override/outcome, còn hạn nghiêm ngặt, không tính <c>SHADOW</c>),
/// gộp theo chi nhánh bằng một truy vấn; chi nhánh không có bản nào chờ thì không có mặt.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class PendingApprovalsCountTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public PendingApprovalsCountTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    private static EfDecisionAuditStore StoreOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionAuditStore>.Instance);

    private async Task<Guid> SeedAsync(
        Guid branchId,
        Guid reservationId,
        DateTimeOffset createdAt,
        string status = "NEED_APPROVAL",
        string role = "PRIMARY",
        DateTimeOffset? expiresAt = null)
    {
        var id = Guid.NewGuid();
        var row = AuditSchemaSql.ValidExecution(id);
        row["branch_id"] = branchId;
        row["reservation_id"] = reservationId;
        row["role"] = role;
        row["status"] = status;
        row["strategy_status"] = status;
        row["created_at"] = createdAt;
        if (status == "NEED_APPROVAL")
        {
            row["deposit_rate"] = null;
            row["deposit_amount"] = null;
            row["approval_expires_at"] = expiresAt ?? Now.AddHours(1);
        }

        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Executions, row);
        return id;
    }

    /// <summary>Gài vào <paramref name="branch"/> một tập bản ghi gồm cả bản tính và không tính vào hàng chờ; trả số bản kỳ vọng được tính.</summary>
    private async Task<int> SeedMixedAsync(Guid branch)
    {
        var t0 = Now.AddHours(-3);
        var t1 = Now.AddHours(-2);

        // Tính: hai đơn khác nhau còn hạn, một đơn có bản mới hơn cùng là NEED_APPROVAL (chỉ bản mới tính)
        await SeedAsync(branch, Guid.NewGuid(), t0);
        await SeedAsync(branch, Guid.NewGuid(), t0);
        var replaced = Guid.NewGuid();
        await SeedAsync(branch, replaced, t0);
        await SeedAsync(branch, replaced, t1);

        // Không tính: đã hết hạn, bản APPROVED, bản bị bản APPROVED mới hơn thay thế, SHADOW, đã override, đã có outcome
        await SeedAsync(branch, Guid.NewGuid(), t0, expiresAt: Now);
        await SeedAsync(branch, Guid.NewGuid(), t0, status: "APPROVED");
        var superseded = Guid.NewGuid();
        await SeedAsync(branch, superseded, t0);
        await SeedAsync(branch, superseded, t1, status: "APPROVED");
        await SeedAsync(branch, Guid.NewGuid(), t0, role: "SHADOW");
        var overridden = await SeedAsync(branch, Guid.NewGuid(), t0);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Overrides, AuditSchemaSql.ValidOverride(overridden));
        var finished = await SeedAsync(branch, Guid.NewGuid(), t0);
        await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Outcomes, AuditSchemaSql.ValidOutcome(finished));

        return 3;
    }

    /// <summary>Với nhiều chi nhánh và dữ liệu hỗn hợp, số đếm gộp của mỗi chi nhánh bằng <c>TotalItems</c> của truy vấn hàng chờ cùng <c>nowUtc</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task CountPendingApprovalsByBranchAsync_MatchesPendingQuery()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var expectedA = await SeedMixedAsync(branchA);
        var expectedB = await SeedMixedAsync(branchB);
        await SeedAsync(branchB, Guid.NewGuid(), Now.AddHours(-1));
        expectedB++;

        await using var db = CreateDbContext();
        var store = StoreOver(db);
        var counts = await store.CountPendingApprovalsByBranchAsync(Now, CancellationToken.None);
        var totalA = (await store.GetPendingApprovalsAsync(branchA, 1, 100, Now, CancellationToken.None)).TotalItems;
        var totalB = (await store.GetPendingApprovalsAsync(branchB, 1, 100, Now, CancellationToken.None)).TotalItems;

        counts[branchA].Should().Be(expectedA).And.Be(totalA);
        counts[branchB].Should().Be(expectedB).And.Be(totalB);
    }

    /// <summary>Chi nhánh chỉ có bản không tính (hết hạn, APPROVED, SHADOW) không có mặt trong kết quả; không có dữ liệu nào thì kết quả rỗng.</summary>
    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task CountPendingApprovalsByBranchAsync_BranchWithoutPending_IsAbsent()
    {
        var branch = Guid.NewGuid();
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-3), expiresAt: Now.AddMinutes(-1));
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-3), status: "APPROVED");
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-3), role: "SHADOW");

        await using var db = CreateDbContext();
        var counts = await StoreOver(db).CountPendingApprovalsByBranchAsync(Now, CancellationToken.None);

        counts.Should().NotContainKey(branch);
    }

    /// <summary>Mốc <c>nowUtc</c> quyết định hạn duyệt (so nghiêm ngặt): đúng bằng hạn thì không tính, sớm hơn một micro giây thì tính.</summary>
    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task CountPendingApprovalsByBranchAsync_ExpiryComparedStrictlyAgainstNow()
    {
        var branch = Guid.NewGuid();
        var expires = Now.AddMinutes(10);
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-1), expiresAt: expires);

        await using var db = CreateDbContext();
        var store = StoreOver(db);
        var before = await store.CountPendingApprovalsByBranchAsync(expires - TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond), CancellationToken.None);
        var atExpiry = await store.CountPendingApprovalsByBranchAsync(expires, CancellationToken.None);

        before[branch].Should().Be(1);
        atExpiry.Should().NotContainKey(branch);
    }
}
