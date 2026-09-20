using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.MockAlternative;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// Bộ kiểm thử xác thực tính chính xác và các biên quyết định của MockAlternativeDecisionStrategy.
/// </summary>
public class MockAlternativeDecisionStrategyTests
{
    private readonly MockAlternativeDecisionStrategy _strategy = new();

    private static Reservation CreateReservation(
        DateTime startTime,
        decimal totalPrice = 200000m,
        decimal depositAmount = 100000m,
        string customerTier = "Standard",
        PaymentStatus paymentStatus = PaymentStatus.PARTIALLY_PAID)
    {
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
        _strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        _strategy.StrategyVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationIsNull_ThrowsArgumentNullException()
    {
        var act = () => _strategy.EvaluateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_FullyPaidBooking_AlwaysApproved_RegardlessOfHour()
    {
        // Arrange: Peak hour (Wednesday 18:00) with 100% deposit
        var peakWednesday = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc); // Wednesday 18:00
        var reservation = CreateReservation(peakWednesday, 200000m, 200000m, "Standard", PaymentStatus.PAID);

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedPaidInFull);
        result.FallbackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_VipCustomer_AlwaysApproved_RegardlessOfHourAndDeposit()
    {
        // Arrange: Peak hour (Wednesday 18:00), VIP tier, 0 deposit
        var peakWednesday = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateReservation(peakWednesday, 200000m, 0m, "VIP", PaymentStatus.UNPAID);

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.AutoApprovedTierEligible);
    }

    [Fact]
    public async Task EvaluateAsync_WeekdayPeakHour_WithSufficientDeposit50Percent_ReturnsApproved()
    {
        // Arrange: Wednesday 18:00 (Peak), exactly 50% deposit
        var peakWednesday = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateReservation(peakWednesday, 200000m, 100000m); // 50%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedStandard);
        result.Reasons.Should().Contain(r => r.Contains("50.0%"));
    }

    [Fact]
    public async Task EvaluateAsync_WeekdayPeakHour_WithInsufficientDeposit_ReturnsNeedApprovalAndRecommendations()
    {
        // Arrange: Wednesday 18:00 (Peak), only 30% deposit (needs 50% for peak)
        var peakWednesday = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var reservation = CreateReservation(peakWednesday, 200000m, 60000m); // 30%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.DepositBelowThreshold);

        // Recommendations check: MUST contain REQUIRE_DEPOSIT and ALTERNATIVE_SLOT
        result.Recommendations.Should().HaveCount(2);

        var requireDeposit = result.Recommendations.FirstOrDefault(r => r.Type == RecommendationType.REQUIRE_DEPOSIT.ToString());
        requireDeposit.Should().NotBeNull();
        requireDeposit!.Description.Should().Contain("40,000 VND"); // 100,000 - 60,000 = 40,000 shortage
        requireDeposit.ConfidenceScore.Should().Be(1.0);

        var alternativeSlot = result.Recommendations.FirstOrDefault(r => r.Type == RecommendationType.ALTERNATIVE_SLOT.ToString());
        alternativeSlot.Should().NotBeNull();
        alternativeSlot!.Description.Should().Contain("khung giờ thấp điểm");
        alternativeSlot.ConfidenceScore.Should().Be(0.85);
    }

    [Fact]
    public async Task EvaluateAsync_WeekendPeakHour_WithSufficientDeposit50Percent_ReturnsApproved()
    {
        // Arrange: Saturday 10:00 (Weekend Peak 08:00 - 21:00), 50% deposit
        var saturday = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc); // Saturday
        var reservation = CreateReservation(saturday, 300000m, 150000m); // 50%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedStandard);
    }

    [Fact]
    public async Task EvaluateAsync_WeekendPeakHour_WithInsufficientDeposit_ReturnsNeedApproval()
    {
        // Arrange: Sunday 14:00 (Weekend Peak), only 30% deposit
        var sunday = new DateTime(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc); // Sunday
        var reservation = CreateReservation(sunday, 300000m, 90000m); // 30%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.DepositBelowThreshold);
    }

    [Fact]
    public async Task EvaluateAsync_WeekdayOffPeak_WithSufficientDeposit30Percent_ReturnsApproved()
    {
        // Arrange: Wednesday 10:00 (Off-Peak: before 17:00), 30% deposit
        var offPeakWednesday = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        var reservation = CreateReservation(offPeakWednesday, 200000m, 60000m); // 30%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.ApprovedStandard);
        result.Reasons.Should().Contain(r => r.Contains("30.0%"));
    }

    [Fact]
    public async Task EvaluateAsync_WeekdayOffPeak_WithInsufficientDepositBelow30Percent_ReturnsNeedApproval()
    {
        // Arrange: Wednesday 10:00 (Off-Peak), only 10% deposit (below 30%)
        var offPeakWednesday = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
        var reservation = CreateReservation(offPeakWednesday, 200000m, 20000m); // 10%

        // Act
        var result = await _strategy.EvaluateAsync(reservation);

        // Assert
        result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        result.PrimaryReason.Should().Be(ErrorCodes.DepositBelowThreshold);

        // Off-peak shortage: 60,000 - 20,000 = 40,000 shortage to 30%
        result.Recommendations.Should().HaveCount(1);
        result.Recommendations[0].Type.Should().Be(RecommendationType.REQUIRE_DEPOSIT.ToString());
        result.Recommendations[0].Description.Should().Contain("40,000 VND");
    }

    [Theory]
    [InlineData(2026, 9, 23, 17, 0, 0, true)]   // Wednesday 17:00:00 -> Peak
    [InlineData(2026, 9, 23, 16, 59, 59, false)] // Wednesday 16:59:59 -> Off-peak
    [InlineData(2026, 9, 23, 20, 59, 59, true)]  // Wednesday 20:59:59 -> Peak
    [InlineData(2026, 9, 23, 21, 0, 0, false)]   // Wednesday 21:00:00 -> Off-peak
    [InlineData(2026, 9, 26, 8, 0, 0, true)]     // Saturday 08:00:00 -> Weekend Peak
    [InlineData(2026, 9, 26, 7, 59, 59, false)]  // Saturday 07:59:59 -> Weekend Off-peak
    [InlineData(2026, 9, 26, 20, 59, 59, true)]  // Saturday 20:59:59 -> Weekend Peak
    [InlineData(2026, 9, 26, 21, 0, 0, false)]   // Saturday 21:00:00 -> Weekend Off-peak
    public void IsPeakHour_BoundaryEvaluation_ReturnsExpectedClassification(
        int year, int month, int day, int hour, int minute, int second, bool expectedPeak)
    {
        var dt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
        MockAlternativeDecisionStrategy.IsPeakHour(dt).Should().Be(expectedPeak);
    }
}
