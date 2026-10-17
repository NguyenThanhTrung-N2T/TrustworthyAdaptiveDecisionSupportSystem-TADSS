using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03: quy tắc từ chối của <c>DefaultDecisionStrategy</c> 2.1.0 — khách giữ chỗ rồi để hết hạn thanh toán nhiều lần mà số lần đó vượt
/// số lần hoàn tất thì <c>REJECTED</c>; biên ngưỡng, điều kiện phụ, tắt bằng cấu hình, thứ tự so với các quy tắc duyệt, lý do trung tính.
/// </summary>
public class DefaultDecisionStrategyRejectionTests
{
    private static readonly DefaultDecisionStrategy Strategy = new(new DefaultStrategyOptions());

    private static Task<StrategyDecision> Evaluate(
        DefaultDecisionStrategy strategy, int paymentExpired, int completed = 0, int noShow = 0, long totalPrice = 228_000, bool isGuest = false)
    {
        var history = DomainTestData.CreateHistory(noShow, completed, paymentExpired);
        var customer = isGuest ? DomainTestData.CreateGuest(history) : DomainTestData.CreateMember(CustomerTier.GOLD, history);
        var reservation = DomainTestData.CreateReservation(customer: customer, totalPrice: totalPrice);
        return strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);
    }

    /// <summary>Biên ngưỡng mặc định 5 và điều kiện phụ "số lần hết hạn lớn hơn số lần hoàn tất".</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(4, 0, DecisionStatus.APPROVED)]
    [InlineData(5, 0, DecisionStatus.REJECTED)]
    [InlineData(6, 0, DecisionStatus.REJECTED)]
    [InlineData(5, 4, DecisionStatus.REJECTED)]
    [InlineData(5, 5, DecisionStatus.APPROVED)]
    [InlineData(5, 30, DecisionStatus.APPROVED)]
    [InlineData(40, 39, DecisionStatus.REJECTED)]
    [InlineData(40, 40, DecisionStatus.APPROVED)]
    public async Task EvaluateAsync_PaymentExpiryBoundaries_RejectsOnlyWhenThresholdReachedAndDominant(int paymentExpired, int completed, DecisionStatus expected)
    {
        var decision = await Evaluate(Strategy, paymentExpired, completed);

        decision.Status.Should().Be(expected);
        if (expected == DecisionStatus.REJECTED)
        {
            decision.ReasonCode.Should().Be(DefaultDecisionStrategy.RepeatedPaymentExpiryReasonCode);
            decision.DepositRate.Should().BeNull();
        }
    }

    /// <summary>Lý do trung tính: nêu số đếm và ngưỡng, không quy kết gian lận.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_Rejected_ReasonStatesCountsNeutrally()
    {
        var decision = await Evaluate(Strategy, paymentExpired: 7, completed: 1);

        decision.ReasonCode.Should().Be("REPEATED_PAYMENT_EXPIRY");
        decision.Reasons.Should().ContainSingle().Which.Should().Contain("7").And.Contain("1").And.Contain("ngưỡng 5").And.NotContain("gian lận");
        decision.Recommendations.Should().BeEmpty();
    }

    /// <summary>Khách vãng lai cũng bị xét theo lịch sử (lịch sử ghép theo số điện thoại).</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_GuestWithRepeatedExpiry_IsRejected()
    {
        var decision = await Evaluate(Strategy, paymentExpired: 5, isGuest: true);

        decision.Status.Should().Be(DecisionStatus.REJECTED);
    }

    /// <summary>Không có lịch sử thì không đủ bằng chứng để từ chối.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NoHistory_IsNotRejected()
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(CustomerTier.GOLD, history: null));

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
    }

    /// <summary>Ngưỡng 0 tắt quy tắc; ngưỡng tùy chỉnh được áp dụng.</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(0, 50, DecisionStatus.APPROVED)]
    [InlineData(2, 1, DecisionStatus.APPROVED)]
    [InlineData(2, 2, DecisionStatus.REJECTED)]
    [InlineData(10, 9, DecisionStatus.APPROVED)]
    [InlineData(10, 10, DecisionStatus.REJECTED)]
    public async Task EvaluateAsync_ConfiguredThreshold_IsApplied(int threshold, int paymentExpired, DecisionStatus expected)
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { PaymentExpiryRejectThreshold = threshold });

        var decision = await Evaluate(strategy, paymentExpired);

        decision.Status.Should().Be(expected);
    }

    /// <summary>Thứ tự: từ chối đứng trước mọi quy tắc duyệt (không kéo dài việc giữ slot của khách hay giữ chỗ).</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(5, 0, 228_000L, DecisionStatus.REJECTED, "REPEATED_PAYMENT_EXPIRY")]
    [InlineData(5, 2, 228_000L, DecisionStatus.REJECTED, "REPEATED_PAYMENT_EXPIRY")]
    [InlineData(5, 0, 2_500_000L, DecisionStatus.REJECTED, "REPEATED_PAYMENT_EXPIRY")]
    [InlineData(5, 3, 2_500_000L, DecisionStatus.REJECTED, "REPEATED_PAYMENT_EXPIRY")]
    [InlineData(0, 3, 2_500_000L, DecisionStatus.NEED_APPROVAL, ReasonCodes.HighValueReview)]
    [InlineData(0, 3, 228_000L, DecisionStatus.NEED_APPROVAL, ReasonCodes.NoShowHistoryReview)]
    [InlineData(0, 0, 228_000L, DecisionStatus.APPROVED, ReasonCodes.TierDeposit)]
    public async Task EvaluateAsync_RuleOrder_RejectionBeatsApprovalRules(int paymentExpired, int noShow, long totalPrice, DecisionStatus expected, string reason)
    {
        var decision = await Evaluate(Strategy, paymentExpired, noShow: noShow, totalPrice: totalPrice);

        decision.Status.Should().Be(expected);
        decision.ReasonCode.Should().Be(reason);
    }

    /// <summary>Cùng đầu vào cho cùng kết quả (tất định); phiên bản strategy là 2.1.0.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_SameInput_IsDeterministicAndVersionIs210()
    {
        var first = await Evaluate(Strategy, paymentExpired: 6);
        var second = await Evaluate(Strategy, paymentExpired: 6);

        second.Status.Should().Be(first.Status);
        second.ReasonCode.Should().Be(first.ReasonCode);
        second.Reasons.Should().Equal(first.Reasons);
        Strategy.StrategyVersion.Should().Be("2.1.0");
    }

    /// <summary>Ngưỡng âm bị từ chối khi dựng strategy.</summary>
    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Constructor_NegativePaymentExpiryThreshold_Throws()
    {
        var act = () => new DefaultDecisionStrategy(new DefaultStrategyOptions { PaymentExpiryRejectThreshold = -1 });

        act.Should().Throw<ArgumentException>().WithMessage("*PaymentExpiryRejectThreshold*");
    }
}
