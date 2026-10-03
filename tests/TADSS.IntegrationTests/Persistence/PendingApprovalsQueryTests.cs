using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Infrastructure.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.IntegrationTests.Migrations;
using Xunit;

namespace TADSS.IntegrationTests.Persistence;

/// <summary>
/// IT-09: hàng chờ duyệt (<see cref="IDecisionQueryStore.GetPendingApprovalsAsync"/>) trên PostgreSQL thật — ngữ nghĩa lọc
/// (bản mới hơn thay bản cũ, chi nhánh, hạn duyệt nghiêm ngặt, override/outcome, SHADOW, hòa <c>created_at</c>), phân trang tất định kèm tổng,
/// và kế hoạch thực thi dùng partial index <c>idx_de_pending</c>.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class PendingApprovalsQueryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond);

    public PendingApprovalsQueryTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    private static EfDecisionAuditStore StoreOver(TadssDbContext db) =>
        new(db, NullLogger<EfDecisionAuditStore>.Instance);

    /// <summary>Gài một bản ghi quyết định bằng SQL trực tiếp (kiểm soát từng cột); trả <c>decision_id</c>.</summary>
    private async Task<Guid> SeedAsync(
        Guid branchId,
        Guid reservationId,
        DateTimeOffset createdAt,
        string status = "NEED_APPROVAL",
        string role = "PRIMARY",
        DateTimeOffset? expiresAt = null,
        Guid? decisionId = null)
    {
        var id = decisionId ?? Guid.NewGuid();
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

    private async Task<PendingApprovalsPage> QueryAsync(Guid branchId, int page = 1, int pageSize = 100)
    {
        await using var db = CreateDbContext();
        return await StoreOver(db).GetPendingApprovalsAsync(branchId, page, pageSize, Now, CancellationToken.None);
    }

    /// <summary>Dựng dữ liệu của một ca và trả tập <c>decision_id</c> kỳ vọng có mặt trong hàng chờ của <paramref name="branch"/>.</summary>
    private async Task<Guid[]> BuildScenarioAsync(string scenario, Guid branch)
    {
        var other = Guid.NewGuid();
        var reservation = Guid.NewGuid();
        var t0 = Now.AddHours(-3);
        var t1 = Now.AddHours(-2);

        switch (scenario)
        {
            case "NewerApprovedReplacesOlder":
                await SeedAsync(branch, reservation, t0);
                await SeedAsync(branch, reservation, t1, status: "APPROVED");
                return [];
            case "NewerNeedApprovalReplacesOlder":
                await SeedAsync(branch, reservation, t0);
                return [await SeedAsync(branch, reservation, t1)];
            case "NewerInOtherBranchDoesNotReplace":
                // Cùng reservation nhưng khác chi nhánh: bản của chi nhánh đang xem không bị thay thế
                var kept = await SeedAsync(branch, reservation, t0);
                await SeedAsync(other, reservation, t1);
                return [kept];
            case "OtherBranchExcluded":
                await SeedAsync(other, reservation, t0);
                return [];
            case "ExpiresExactlyNowExcluded":
                await SeedAsync(branch, reservation, t0, expiresAt: Now);
                return [];
            case "ExpiresOneMicrosecondBeforeNowExcluded":
                await SeedAsync(branch, reservation, t0, expiresAt: Now - Microsecond);
                return [];
            case "ExpiresOneMicrosecondAfterNowIncluded":
                return [await SeedAsync(branch, reservation, t0, expiresAt: Now + Microsecond)];
            case "OverriddenExcluded":
                var overridden = await SeedAsync(branch, reservation, t0);
                await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Overrides, AuditSchemaSql.ValidOverride(overridden));
                return [];
            case "OutcomeRecordedExcluded":
                var finished = await SeedAsync(branch, reservation, t0);
                await AuditSchemaSql.InsertAsync(ConnectionString, AuditSchemaSql.Outcomes, AuditSchemaSql.ValidOutcome(finished));
                return [];
            case "ShadowExcluded":
                await SeedAsync(branch, reservation, t0, role: "SHADOW");
                return [];
            case "NewerShadowDoesNotReplacePrimary":
                var primary = await SeedAsync(branch, reservation, t0);
                await SeedAsync(branch, reservation, t1, role: "SHADOW", status: "APPROVED");
                return [primary];
            case "SameCreatedAtGreaterDecisionIdWins":
                await SeedAsync(branch, reservation, t0, decisionId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
                return [await SeedAsync(branch, reservation, t0, decisionId: Guid.Parse("00000000-0000-0000-0000-000000000002"))];
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Ca kiểm thử không tồn tại.");
        }
    }

    [Theory]
    [InlineData("NewerApprovedReplacesOlder")]
    [InlineData("NewerNeedApprovalReplacesOlder")]
    [InlineData("NewerInOtherBranchDoesNotReplace")]
    [InlineData("OtherBranchExcluded")]
    [InlineData("ExpiresExactlyNowExcluded")]
    [InlineData("ExpiresOneMicrosecondBeforeNowExcluded")]
    [InlineData("ExpiresOneMicrosecondAfterNowIncluded")]
    [InlineData("OverriddenExcluded")]
    [InlineData("OutcomeRecordedExcluded")]
    [InlineData("ShadowExcluded")]
    [InlineData("NewerShadowDoesNotReplacePrimary")]
    [InlineData("SameCreatedAtGreaterDecisionIdWins")]
    [Trait("CatalogId", "IT-09")]
    public async Task PendingApprovals_Semantics_Theory(string scenario)
    {
        var branch = Guid.NewGuid();
        var expected = await BuildScenarioAsync(scenario, branch);

        var page = await QueryAsync(branch);

        page.Items.Select(item => item.DecisionId).Should().BeEquivalentTo(expected, scenario);
        page.TotalItems.Should().Be(expected.Length);
    }

    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task PendingApprovals_ReturnsFullRecordWithApprovalWindow()
    {
        var branch = Guid.NewGuid();
        var id = await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-1), expiresAt: Now.AddMinutes(30).AddTicks(450));

        var page = await QueryAsync(branch);

        var item = page.Items.Should().ContainSingle().Which;
        item.DecisionId.Should().Be(id);
        item.BranchId.Should().Be(branch);
        item.Role.Should().Be(ExecutionRole.PRIMARY);
        item.Status.Should().Be(Shared.DecisionContractSDK.Enums.DecisionStatus.NEED_APPROVAL);
        item.ApprovalExpiresAt.Should().Be(Now.AddMinutes(30).AddTicks(450));
        item.DepositRate.Should().BeNull();
        item.DepositAmount.Should().BeNull();
        JsonNode.Parse(item.ResponsePayload).Should().NotBeNull();
    }

    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task PendingApprovals_Paging_OrderAndTotal()
    {
        const int total = 45;
        var branch = Guid.NewGuid();
        var expected = new List<(DateTimeOffset Expires, Guid Id)>();
        for (var index = 0; index < total; index++)
        {
            // 5 bản liền nhau cùng approval_expires_at để kiểm tra thế hòa theo decision_id
            var expires = Now.AddMinutes(10 + (index / 5) * 7);
            var id = await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-1).AddSeconds(index), expiresAt: expires);
            expected.Add((expires, id));
        }

        // Dữ liệu nhiễu không được lọt vào hàng chờ của chi nhánh
        await SeedAsync(Guid.NewGuid(), Guid.NewGuid(), Now.AddHours(-1));
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-1), expiresAt: Now.AddMinutes(-1));
        await SeedAsync(branch, Guid.NewGuid(), Now.AddHours(-1), status: "APPROVED");

        var expectedOrder = expected
            .OrderBy(entry => entry.Expires)
            .ThenBy(entry => entry.Id.ToString(), StringComparer.Ordinal)
            .Select(entry => entry.Id)
            .ToList();

        var pages = new List<PendingApprovalsPage>();
        for (var number = 1; number <= 4; number++)
        {
            pages.Add(await QueryAsync(branch, page: number, pageSize: 20));
        }

        pages.Select(page => page.Items.Count).Should().Equal(20, 20, 5, 0);
        pages.Select(page => page.TotalItems).Should().OnlyContain(count => count == total);
        pages.SelectMany(page => page.Items).Select(item => item.DecisionId).Should().Equal(expectedOrder);
    }

    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task PendingApprovals_Explain_UsesPartialIndex()
    {
        // 6.000 bản PRIMARY ở 60 chi nhánh, 10% NEED_APPROVAL (mỗi chi nhánh ~10 bản chờ)
        await AuditSchemaSql.ExecuteAsync(ConnectionString, $$"""
            INSERT INTO tadss.decision_executions
                (decision_id, reservation_id, branch_id, role, strategy_name, strategy_version, strategy_status, status,
                 deposit_rate, deposit_amount, reason_code, execution_type, execution_time_ms, fallback_used, cache_hit,
                 approval_expires_at, request_hash, request_payload, response_payload, created_at)
            SELECT gen_random_uuid(), gen_random_uuid(),
                   ('00000000-0000-0000-0000-' || lpad((g % 60)::text, 12, '0'))::uuid,
                   'PRIMARY', 'DefaultDecisionStrategy', '1.0.0',
                   CASE WHEN (g / 60) % 10 = 0 THEN 'NEED_APPROVAL' ELSE 'APPROVED' END,
                   CASE WHEN (g / 60) % 10 = 0 THEN 'NEED_APPROVAL' ELSE 'APPROVED' END,
                   CASE WHEN (g / 60) % 10 = 0 THEN NULL ELSE 0.3 END,
                   CASE WHEN (g / 60) % 10 = 0 THEN NULL ELSE 90000 END,
                   'RULE_APPROVED', 'AUTOMATED', 12, false, false,
                   CASE WHEN (g / 60) % 10 = 0 THEN TIMESTAMPTZ '{{Now:O}}' + interval '2 hours' END,
                   repeat('a', 64), '{}'::jsonb, '{}'::jsonb,
                   TIMESTAMPTZ '{{Now:O}}' - interval '1 day' + g * interval '1 second'
            FROM generate_series(1, 6000) AS g
            """);
        await AuditSchemaSql.ExecuteAsync(ConnectionString, "ANALYZE tadss.decision_executions; ANALYZE tadss.decision_overrides; ANALYZE tadss.decision_outcomes");
        var branch = Guid.Parse("00000000-0000-0000-0000-000000000007");
        (await AuditSchemaSql.ScalarAsync<int>(ConnectionString, "SELECT count(*) FROM tadss.decision_executions")).Should().BeGreaterThanOrEqualTo(5000);

        // Câu trang thật (cùng FormattableString mà store chạy), chạy qua EXPLAIN với tham số
        var statement = PendingApprovalsQuery.PageStatement(branch, Now, pageSize: 20, offset: 0);
        var sql = "EXPLAIN (FORMAT JSON) " + string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            statement.Format,
            Enumerable.Range(0, statement.ArgumentCount).Select(index => (object)("@p" + index)).ToArray());
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var index = 0; index < statement.ArgumentCount; index++)
        {
            command.Parameters.Add(new NpgsqlParameter("p" + index, statement.GetArgument(index)));
        }

        var plan = (string)(await command.ExecuteScalarAsync())!;

        IndexNames(JsonNode.Parse(plan)!).Should().Contain("idx_de_pending", "partial index của hàng chờ phải dùng được:\n" + plan);

        // Cùng dữ liệu: store trả đúng 10 bản chờ của chi nhánh
        var page = await QueryAsync(branch, pageSize: 100);
        page.TotalItems.Should().Be(page.Items.Count).And.BeGreaterThan(0);
    }

    /// <summary>Gom mọi <c>"Index Name"</c> xuất hiện ở bất kỳ nút nào của cây kế hoạch (kể cả subplan).</summary>
    private static HashSet<string> IndexNames(JsonNode node)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(node);
        return names;

        void Collect(JsonNode? current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "Index Name" && value is JsonValue name)
                        {
                            names.Add(name.GetValue<string>());
                        }

                        Collect(value);
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Collect(item);
                    }

                    break;
            }
        }
    }
}
