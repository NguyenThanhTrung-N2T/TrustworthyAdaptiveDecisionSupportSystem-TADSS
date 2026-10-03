using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-01: <c>POST /api/v2/decisions</c> đi qua pipeline HTTP thật trên PostgreSQL và Redis thật — đúng một hàng audit <c>PRIMARY</c>
/// kèm snapshot request/response, response hợp lệ theo schema (CT-01), và trạng thái gốc của strategy được lưu tách khỏi trạng thái
/// trả cho client khi approval window không khả dụng.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class DecisionsEndpointTests : DecisionApiTestBase
{
    public DecisionsEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Request mẫu hợp lệ: 200, một hàng <c>PRIMARY</c> có snapshot request chuẩn hóa và response, correlation id được echo.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task PostDecision_ValidRequest_Returns200AndPersistsOneRecordWithRequestSnapshot()
    {
        var correlationId = "corr-it01-" + Guid.NewGuid().ToString("N");
        var key = NewKey();

        var response = await PostDecisionAsync(DecisionRequests.Valid(), key, correlationId);

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Header(HttpHeaderNames.CorrelationId).Should().Be(correlationId);
        var result = response.Result;
        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        var deposit = ContractSamples.Required(result.Deposit);
        deposit.Rate.Should().Be(0.3m);
        deposit.Amount.Should().Be(68_400m);
        result.EvaluatedAt.Should().Be(InitialInstant);

        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.Role.Should().Be(ExecutionRole.PRIMARY);
        row.DecisionId.Should().Be(result.DecisionId);
        row.IdempotencyKey.Should().Be(key);
        row.CorrelationId.Should().Be(correlationId);
        row.Status.Should().Be(DecisionStatus.APPROVED);
        row.StrategyStatus.Should().Be(DecisionStatus.APPROVED);
        row.CacheHit.Should().BeFalse();
        row.CreatedAt.Should().Be(InitialInstant);
        var normalizedRequest = JsonSerializer.Serialize(ContractSamples.DeserializeRequest(DecisionRequests.Valid()), TadssJson.Options);
        JsonNode.DeepEquals(JsonNode.Parse(row.RequestPayload), JsonNode.Parse(normalizedRequest))
            .Should().BeTrue("request_payload là snapshot request đã chuẩn hóa");
        JsonNode.DeepEquals(JsonNode.Parse(row.ResponsePayload), JsonNode.Parse(response.Body))
            .Should().BeTrue("response_payload là đúng response đã trả cho client");
    }

    /// <summary>
    /// Strategy trả <c>NEED_APPROVAL</c> (đơn giá trị lớn) nhưng giờ bắt đầu còn gần hơn <c>MinLeadMinutes</c>: client nhận <c>APPROVED</c> cọc 100%,
    /// cột <c>strategy_status</c> vẫn là <c>NEED_APPROVAL</c> và GET hiển thị cả hai.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task PostDecision_WindowUnavailableRequest_PersistsStrategyStatusSeparately()
    {
        // 1 giờ nữa mới bắt đầu < MinLeadMinutes (120): không còn chỗ cho bước Manager duyệt
        var request = DecisionRequests.HighValue(InitialInstant.AddHours(1));

        var response = await PostDecisionAsync(request, NewKey());

        response.Status.Should().Be(HttpStatusCode.OK);
        var result = response.Result;
        result.Status.Should().Be(DecisionStatus.APPROVED);
        result.ReasonCode.Should().Be(ReasonCodes.ApprovalWindowUnavailable);
        result.ApprovalExpiresAt.Should().BeNull();
        var deposit = ContractSamples.Required(result.Deposit);
        deposit.Rate.Should().Be(1m);
        deposit.Amount.Should().Be(DecisionRequests.HighValueTotalPrice);

        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.Status.Should().Be(DecisionStatus.APPROVED);
        row.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
        row.DepositRate.Should().Be(1m);
        row.ApprovalExpiresAt.Should().BeNull();

        var detail = await GetDecisionAsync(result.DecisionId);
        detail.Status.Should().Be(HttpStatusCode.OK);
        detail.Detail.Result.Status.Should().Be(DecisionStatus.APPROVED);
        detail.Detail.Result.StrategyStatus.Should().Be(DecisionStatus.NEED_APPROVAL);
    }
}
