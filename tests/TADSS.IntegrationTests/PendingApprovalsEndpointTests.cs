using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-09: <c>GET /api/v2/decisions/pending-approvals</c> trên PostgreSQL thật qua pipeline HTTP thật, đồng hồ giả. Response 200 đã được kiểm theo
/// <c>pending-approvals.schema.json</c> (CT-01). Ngữ nghĩa lọc và kế hoạch dùng index đã kiểm ở <c>PendingApprovalsQueryTests</c>; ở đây kiểm đường HTTP:
/// phân trang theo chi nhánh và các lỗi tham số.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class PendingApprovalsEndpointTests : DecisionApiTestBase
{
    private static readonly Guid BranchA = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001");
    private static readonly Guid BranchB = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000002");

    public PendingApprovalsEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Tạo 3 quyết định chờ ở chi nhánh A (cách nhau 1 phút để hạn duyệt khác nhau) và 1 ở chi nhánh B; trả các quyết định của A theo thứ tự hạn duyệt.</summary>
    private async Task<List<DecisionResultDto>> SeedBranchesAsync()
    {
        var branchA = new List<DecisionResultDto>();
        for (var index = 0; index < 3; index++)
        {
            branchA.Add(await CreatePendingDecisionAsync(BranchA));
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        await CreatePendingDecisionAsync(BranchB);
        return branchA.OrderBy(result => result.ApprovalExpiresAt).ToList();
    }

    /// <summary>
    /// 3 quyết định chờ ở chi nhánh A, 1 ở B: trang 1 (<c>pageSize=2</c>) có 2 mục kèm tổng 3, trang 2 có 1 mục, chi nhánh B chỉ thấy quyết định của B;
    /// thiếu <c>branchId</c>, <c>pageSize=101</c>, <c>page=abc</c> đều là 400 <c>VALIDATION_ERROR</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-09")]
    [InlineData("PageOne")]
    [InlineData("PageTwo")]
    [InlineData("BranchB")]
    [InlineData("MissingBranchId")]
    [InlineData("PageSize101")]
    [InlineData("PageAbc")]
    public async Task PendingApprovals_Endpoint_PagesAndValidates_Theory(string scenario)
    {
        var expectedA = await SeedBranchesAsync();

        switch (scenario)
        {
            case "PageOne":
                var pageOne = await GetPendingApprovalsAsync($"?branchId={BranchA}&page=1&pageSize=2");
                pageOne.Status.Should().Be(HttpStatusCode.OK);
                var first = pageOne.Pending;
                first.Items.Select(item => item.DecisionId).Should().Equal(expectedA.Take(2).Select(result => result.DecisionId));
                (first.Page, first.PageSize, first.TotalItems, first.TotalPages, first.HasNext, first.HasPrev)
                    .Should().Be((1, 2, 3, 2, true, false));
                first.Items.Should().OnlyContain(item => item.BranchId == BranchA && item.Currency == "VND" && item.ReasonCode.Length > 0);
                break;
            case "PageTwo":
                var pageTwo = await GetPendingApprovalsAsync($"?branchId={BranchA}&page=2&pageSize=2");
                pageTwo.Status.Should().Be(HttpStatusCode.OK);
                var second = pageTwo.Pending;
                second.Items.Select(item => item.DecisionId).Should().Equal(expectedA.Skip(2).Select(result => result.DecisionId));
                (second.Page, second.TotalItems, second.TotalPages, second.HasNext, second.HasPrev).Should().Be((2, 3, 2, false, true));
                break;
            case "BranchB":
                var other = await GetPendingApprovalsAsync($"?branchId={BranchB}");
                other.Status.Should().Be(HttpStatusCode.OK);
                var otherPage = other.Pending;
                otherPage.TotalItems.Should().Be(1);
                otherPage.Items.Should().ContainSingle().Which.BranchId.Should().Be(BranchB);
                otherPage.PageSize.Should().Be(20, "mặc định theo contract");
                break;
            default:
                var query = scenario switch
                {
                    "MissingBranchId" => string.Empty,
                    "PageSize101" => $"?branchId={BranchA}&pageSize=101",
                    _ => $"?branchId={BranchA}&page=abc"
                };
                var invalid = await GetPendingApprovalsAsync(query);
                invalid.Status.Should().Be(HttpStatusCode.BadRequest);
                invalid.ContentType.Should().Be("application/problem+json");
                invalid.Problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
                invalid.Problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
                break;
        }
    }
}
