using System.Net;
using System.Text.Json;
using FluentAssertions;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// CT-03 (Schemathesis tìm thấy ở bước d của chế độ đánh giá): <c>decisionId</c> không phải GUID trên ba endpoint có <c>{decisionId}</c> phải trả 404 <c>DECISION_NOT_FOUND</c>
/// dạng <c>application/problem+json</c> như contract, không phải 404 rỗng của routing.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class UnmatchedDecisionRouteTests : DecisionApiTestBase
{
    public UnmatchedDecisionRouteTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>GET/override/outcome với định danh không phải GUID (kể cả dạng Schemathesis sinh ra) → 404 problem+json <c>DECISION_NOT_FOUND</c>.</summary>
    [Theory]
    [InlineData("GET", "/api/v2/decisions/not-a-guid")]
    [InlineData("GET", "/api/v2/decisions/null%2Cnull")]
    [InlineData("POST", "/api/v2/decisions/not-a-guid/override")]
    [InlineData("POST", "/api/v2/decisions/null%2Cnull/outcome")]
    [Trait("CatalogId", "CT-03")]
    public async Task NonGuidDecisionId_Returns404ProblemDetails(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await Api.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(404);
    }

    /// <summary>Sai phương thức trên <c>/api/v2/decisions</c> (không có đoạn sau) không bị route catch-all che: vẫn là 405 của routing.</summary>
    [Fact]
    [Trait("CatalogId", "CT-03")]
    public async Task CollectionRouteWithWrongMethod_StillReturns405()
    {
        using var response = await Api.DeleteAsync("/api/v2/decisions");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    /// <summary>Định danh quá dài chỉ được lặp lại tối đa 64 ký tự trong <c>detail</c>.</summary>
    [Fact]
    [Trait("CatalogId", "CT-03")]
    public async Task VeryLongIdentifier_IsTruncatedInDetail()
    {
        var longId = new string('x', 200);

        using var response = await Api.GetAsync($"/api/v2/decisions/{longId}");

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().Should().NotContain(longId).And.Contain(new string('x', 64));
    }
}
