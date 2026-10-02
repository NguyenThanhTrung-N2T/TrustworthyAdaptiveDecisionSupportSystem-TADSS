using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-01: <c>GET /api/v2/decisions/{decisionId}</c> trên PostgreSQL thật — chi tiết quyết định vừa ghi hợp lệ theo
/// <c>decision-detail.schema.json</c> (CT-01) và <c>DECISION_NOT_FOUND</c> cho định danh lạ.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class DecisionDetailEndpointTests : DecisionApiTestBase
{
    public DecisionDetailEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>POST rồi GET: chi tiết khớp quyết định đã ghi, chưa có override và outcome.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDecision_ExistingDecision_ReturnsDetailValidAgainstSchema()
    {
        var correlationId = "corr-detail-" + Guid.NewGuid().ToString("N");
        var posted = await PostDecisionAsync(DecisionRequests.Valid(), NewKey(), correlationId);
        posted.Status.Should().Be(HttpStatusCode.OK);
        var decisionId = posted.Result.DecisionId;

        var response = await GetDecisionAsync(decisionId);

        response.Status.Should().Be(HttpStatusCode.OK);
        var detail = response.Detail;
        detail.DecisionId.Should().Be(decisionId);
        detail.ReservationId.Should().Be(posted.Result.ReservationId);
        detail.Override.Should().BeNull();
        detail.Outcome.Should().BeNull();
        detail.CorrelationId.Should().Be(correlationId);
        detail.CreatedAt.Should().Be(InitialInstant);
        detail.Result.Status.Should().Be(DecisionStatus.APPROVED);
        detail.Result.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        detail.Result.Deposit.Should().BeEquivalentTo(posted.Result.Deposit);
        detail.Execution.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        detail.Execution.CacheHit.Should().BeFalse();
        detail.Request.ReservationId.Should().Be(posted.Result.ReservationId);
    }

    /// <summary>Guid ngẫu nhiên không có trong database: 404 problem+json <c>DECISION_NOT_FOUND</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDecision_UnknownId_Returns404DecisionNotFound()
    {
        var unknownId = Guid.NewGuid();

        var response = await GetDecisionAsync(unknownId);

        response.Status.Should().Be(HttpStatusCode.NotFound);
        response.ContentType.Should().Be("application/problem+json");
        var problem = response.Problem;
        problem.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        problem.GetProperty("status").GetInt32().Should().Be(404);
        problem.GetProperty("detail").GetString().Should().Contain(unknownId.ToString());
    }
}
