using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// Bộ kiểm thử xác thực các nhánh quyết định của DefaultDecisionStrategy,
/// bao gồm nguyên tắc mặc định đưa về yêu cầu thanh toán 100% khi không thỏa mãn tiêu chí giảm cọc.
/// </summary>
public class DefaultDecisionStrategyTests
{
    private readonly DefaultDecisionStrategy _strategy = new();

    private static Reservation CreateReservation(
        decimal totalPrice = 200000m,
        decimal depositAmount = 100000m,
        string customerTier = "Standard",
        PaymentStatus paymentStatus = PaymentStatus.PARTIALLY_PAID)
    {
        var startTime = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: customerTier,
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: startTime,
            endTime: startTime.AddHours(2),
            totalPrice: totalPrice,
            depositAmount: depositAmount,
            paymentStatus: paymentStatus,
            bookingType: "SINGLE",
            priority: BookingPriority.NORMAL,
            requestedAt: DateTime.UtcNow);
    }

    [Fact]
    public void StrategyIdentity_NameAndVersion_AreCorrect()
    {
        _strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        _strategy.StrategyVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationIsNull_ThrowsArgumentNullException()
    {
        var act = () => _strategy.EvaluateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_FullyPaidBooking_ReturnsApprovedPaidInFull()
    {
        var reservation = CreateReservation(totalPrice: 300000m, depositAmount: 300000m, customerTier: "Standard", paymentStatus: PaymentStatus.PAID);

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedPaidInFull);
        result.FallbackUsed.Should().BeFalse();
        result.Recommendations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("VIP")]
    [InlineData("DIAMOND")]
    [InlineData("PLATINUM")]
    public async Task EvaluateAsync_PremiumTierCustomer_ReturnsApprovedRegardlessOfDeposit(string tier)
    {
        var reservation = CreateReservation(totalPrice: 500000m, depositAmount: 0m, customerTier: tier, paymentStatus: PaymentStatus.UNPAID);

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.AutoApprovedTierEligible);
    }

    [Theory]
    [InlineData("GOLD")]
    [InlineData("VÀNG")]
    public async Task EvaluateAsync_GoldCustomerWith30PercentDeposit_ReturnsApproved(string tier)
    {
        var reservation = CreateReservation(totalPrice: 300000m, depositAmount: 90000m, customerTier: tier);

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.AutoApprovedTierEligible);
    }

    [Fact]
    public async Task EvaluateAsync_HighValueBookingBelow50PercentDeposit_ReturnsNeedApprovalManualReview()
    {
        var reservation = CreateReservation(totalPrice: 2_500_000m, depositAmount: 500_000m, customerTier: "Standard");

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ManualReviewHighValue);
        result.Recommendations.Should().ContainSingle(r =>
            r.Type == RecommendationType.REQUIRE_DEPOSIT.ToString() &&
            r.SuggestedPrice == 2_500_000m);
    }

    [Fact]
    public async Task EvaluateAsync_StandardCustomerWith50PercentDeposit_ReturnsApprovedStandard()
    {
        var reservation = CreateReservation(totalPrice: 400000m, depositAmount: 200000m, customerTier: "Standard");

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedStandard);
    }

    [Theory]
    [InlineData("Standard", 1_000_000, 400_000, "600,000 VND")] // 40% deposit -> defaults to 100% (shortage 600,000 VND, never negative)
    [InlineData("Standard", 300_000, 30_000, "270,000 VND")]    // 10% deposit -> defaults to 100% (shortage 270,000 VND)
    [InlineData("GOLD", 500_000, 100_000, "400,000 VND")]       // Gold with 20% (< 30%) -> defaults to 100% (shortage 400,000 VND)
    public async Task EvaluateAsync_NoReductionCriteriaMet_DefaultsTo100PercentRequirementWithPositiveShortage(
        string tier,
        decimal totalPrice,
        decimal depositAmount,
        string expectedShortageText)
    {
        var reservation = CreateReservation(totalPrice: totalPrice, depositAmount: depositAmount, customerTier: tier);

        var result = await _strategy.EvaluateAsync(reservation);

        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.DepositBelowThreshold);
        result.Reasons.Should().ContainSingle(r => r.Contains("100%"));
        result.Recommendations.Should().ContainSingle();

        var rec = result.Recommendations[0];
        rec.Type.Should().Be(RecommendationType.REQUIRE_DEPOSIT.ToString());
        rec.SuggestedPrice.Should().Be(totalPrice);
        rec.Description.Should().Contain(expectedShortageText);
        rec.Description.Should().Contain("100%");
        rec.Description.Should().NotContain("-"); // Shortage must never be negative
    }
}
