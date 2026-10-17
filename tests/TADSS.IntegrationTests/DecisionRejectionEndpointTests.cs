using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-01: quyết định <c>REJECTED</c> của strategy mặc định đi qua pipeline HTTP thật — khách lặp lại việc giữ chỗ rồi để hết hạn thanh toán bị từ chối:
/// response hợp lệ theo schema (không có cọc, không có hạn duyệt), bản audit <c>PRIMARY</c> ghi đủ trạng thái, mã lý do và phiên bản strategy,
/// gửi lại cùng khóa idempotency trả đúng quyết định cũ, và quyết định không vào hàng chờ duyệt (không có gì để ghi đè).
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class DecisionRejectionEndpointTests : DecisionApiTestBase
{
    public DecisionRejectionEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    private static string RequestWithHistory(int completed, int paymentExpired) => DecisionRequests.Mutated(root =>
    {
        var history = ContractSamples.Object(ContractSamples.Object(root["customer"])["history"]);
        history["completedCount"] = completed;
        history["paymentExpiredCount"] = paymentExpired;
    });

    /// <summary>Lịch sử có 6 lần hết hạn thanh toán và 2 lần hoàn tất: 200 <c>REJECTED</c>, không cọc, không hạn duyệt, một bản audit đủ thông tin.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task PostDecision_RepeatedPaymentExpiry_ReturnsRejectedAndAuditsReason()
    {
        var key = NewKey();

        var response = await PostDecisionAsync(RequestWithHistory(completed: 2, paymentExpired: 6), key);

        response.Status.Should().Be(HttpStatusCode.OK);
        var result = response.Result;
        result.Status.Should().Be(DecisionStatus.REJECTED);
        result.ReasonCode.Should().Be("REPEATED_PAYMENT_EXPIRY");
        result.Deposit.Should().BeNull();
        result.ApprovalExpiresAt.Should().BeNull();
        result.ExecutionType.Should().Be(ExecutionType.AUTOMATED);

        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.Role.Should().Be(ExecutionRole.PRIMARY);
        row.Status.Should().Be(DecisionStatus.REJECTED);
        row.StrategyStatus.Should().Be(DecisionStatus.REJECTED);
        row.ReasonCode.Should().Be("REPEATED_PAYMENT_EXPIRY");
        row.StrategyVersion.Should().Be("2.1.0");
        row.IdempotencyKey.Should().Be(key);
    }

    /// <summary>Gửi lại cùng khóa và cùng nội dung: nhận đúng quyết định đã lưu, vẫn một bản ghi.</summary>
    [Fact]
    [Trait("CatalogId", "IT-02")]
    public async Task PostDecision_RejectedReplayedWithSameKey_ReturnsStoredDecision()
    {
        var key = NewKey();
        var body = RequestWithHistory(completed: 0, paymentExpired: 5);
        var first = await PostDecisionAsync(body, key);

        var second = await PostDecisionAsync(body, key);

        second.Result.DecisionId.Should().Be(first.Result.DecisionId);
        second.Result.Status.Should().Be(DecisionStatus.REJECTED);
        (await ReadExecutionsAsync()).Should().ContainSingle();
    }

    /// <summary>Số lần hết hạn không vượt số lần hoàn tất (khách quen): vẫn được chấp thuận như thường.</summary>
    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task PostDecision_ExpiryNotDominant_IsStillApproved()
    {
        var response = await PostDecisionAsync(RequestWithHistory(completed: 20, paymentExpired: 6), NewKey());

        response.Result.Status.Should().Be(DecisionStatus.APPROVED);
        response.Result.Deposit.Should().NotBeNull();
    }
}
