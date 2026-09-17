using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Domain;

/// <summary>
/// Bộ kiểm thử đơn vị toàn diện cho Rich Domain Models và Invariant Rules của TADSS.
/// Đảm bảo tất cả các nhánh điều kiện, giá trị biên, bảo mật DoS và bất biến đồng thời (Thread-Safety) được bảo vệ chặt chẽ.
/// </summary>
public class DomainInvariantsTests
{
    #region 1. Identity Invariant Tests

    [Fact]
    public void Reservation_EmptyReservationId_ThrowsDomainInvariantException()
    {
        var act = () => Reservation.Create(
            reservationId: Guid.Empty,
            customerId: Guid.NewGuid(),
            customerTier: "STANDARD",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: DateTime.UtcNow.AddDays(1),
            endTime: DateTime.UtcNow.AddDays(1).AddHours(2),
            totalPrice: 200000m,
            depositAmount: 100000m,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: "SINGLE",
            priority: BookingPriority.NORMAL,
            requestedAt: DateTime.UtcNow);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_RESERVATION_ID");
    }

    [Fact]
    public void CustomerInfo_EmptyCustomerId_ThrowsDomainInvariantException()
    {
        var act = () => new CustomerInfo(Guid.Empty, "STANDARD");

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_CUSTOMER_ID");
    }

    [Fact]
    public void CustomerInfo_TierExceedingMaxLength_ThrowsDomainInvariantException()
    {
        var longTier = new string('B', 51);
        var act = () => new CustomerInfo(Guid.NewGuid(), longTier);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_CUSTOMER_TIER_LENGTH");
    }

    [Fact]
    public void Reservation_BookingTypeExceedingMaxLength_ThrowsDomainInvariantException()
    {
        var longBookingType = new string('A', 51);
        var act = () => CreateValidReservation(bookingType: longBookingType);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_BOOKING_TYPE_LENGTH");
    }

    [Fact]
    public void ResourceInfo_EmptyCourtId_ThrowsDomainInvariantException()
    {
        var act = () => new ResourceInfo(Guid.Empty, Guid.NewGuid());

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_COURT_ID");
    }

    [Fact]
    public void ResourceInfo_EmptyBranchId_ThrowsDomainInvariantException()
    {
        var act = () => new ResourceInfo(Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_BRANCH_ID");
    }

    #endregion

    #region 2. Schedule Invariant Tests & Boundaries

    [Fact]
    public void ReservationSchedule_EndTimeBeforeStartTime_ThrowsDomainInvariantException()
    {
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = DateTime.UtcNow.AddHours(1);

        var act = () => new ReservationSchedule(startTime, endTime);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_RANGE");
    }

    [Fact]
    public void ReservationSchedule_DurationLessThan30Minutes_ThrowsDomainInvariantException()
    {
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddMinutes(29); // < 30m

        var act = () => new ReservationSchedule(startTime, endTime);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    [Fact]
    public void ReservationSchedule_DurationExactly30Minutes_Succeeds()
    {
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddMinutes(30);

        var schedule = new ReservationSchedule(startTime, endTime);

        schedule.Duration.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void ReservationSchedule_DurationExactly12Hours_Succeeds()
    {
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddHours(12);

        var schedule = new ReservationSchedule(startTime, endTime);

        schedule.Duration.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void ReservationSchedule_DurationExceeding12Hours_ThrowsDomainInvariantException()
    {
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddHours(12).AddMinutes(1);

        var act = () => new ReservationSchedule(startTime, endTime);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    #endregion

    #region 3. Payment Invariant Tests & Edge Cases

    [Fact]
    public void PaymentPolicy_NegativeTotalPrice_ThrowsDomainInvariantException()
    {
        var act = () => new PaymentPolicy(-1000m, 0m, PaymentStatus.UNPAID);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_TOTAL_PRICE");
    }

    [Fact]
    public void PaymentPolicy_NegativeDepositAmount_ThrowsDomainInvariantException()
    {
        var act = () => new PaymentPolicy(100000m, -50000m, PaymentStatus.UNPAID);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_DEPOSIT_AMOUNT");
    }

    [Fact]
    public void PaymentPolicy_DepositExceedsTotalPrice_ThrowsDomainInvariantException()
    {
        var totalPrice = 200000m;
        var depositAmount = 300000m;

        var act = () => new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PARTIALLY_PAID);

        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Fact]
    public void PaymentPolicy_DepositEqualsTotalPrice_IsFullyPaidTrue()
    {
        var policy = new PaymentPolicy(200000m, 200000m, PaymentStatus.PAID);

        policy.IsFullyPaid.Should().BeTrue();
        policy.DepositPercentage.Should().Be(100m);
    }

    [Fact]
    public void PaymentPolicy_ZeroPriceReservation_SucceedsWithFullSatisfaction()
    {
        var policy = new PaymentPolicy(0m, 0m, PaymentStatus.PAID);

        policy.IsFullyPaid.Should().BeTrue();
        policy.DepositPercentage.Should().Be(100m);
    }

    #endregion

    #region 4. Rich Domain Methods (Deposit Ratio & Cancellation Policy)

    [Fact]
    public void CalculateDepositRatio_NormalPrice_CalculatesCorrectDecimal()
    {
        var reservation = CreateValidReservation(totalPrice: 400000m, depositAmount: 100000m);

        var ratio = reservation.CalculateDepositRatio();

        ratio.Should().Be(0.25m);
    }

    [Fact]
    public void CalculateDepositRatio_ZeroTotalPrice_ReturnsOnePerBusinessConvention()
    {
        var reservation = CreateValidReservation(totalPrice: 0m, depositAmount: 0m);

        var ratio = reservation.CalculateDepositRatio();

        ratio.Should().Be(1.0m);
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_MoreThan24HoursBeforeStart_ReturnsTrue()
    {
        var startTime = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var reservation = CreateValidReservation(startTime: startTime);
        var cancelTime = startTime.AddHours(-25); // 25 hours before

        var canCancel = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        canCancel.Should().BeTrue();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_Exactly24HoursBeforeStart_ReturnsTrue()
    {
        var startTime = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var reservation = CreateValidReservation(startTime: startTime);
        var cancelTime = startTime.AddHours(-24); // Exactly 24 hours

        var canCancel = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        canCancel.Should().BeTrue();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_LessThan24HoursBeforeStart_ReturnsFalse()
    {
        var startTime = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var reservation = CreateValidReservation(startTime: startTime);
        var cancelTime = startTime.AddHours(-23).AddMinutes(-59); // 23h 59m before

        var canCancel = reservation.CanBeCancelledWithoutPenalty(cancelTime);

        canCancel.Should().BeFalse();
    }

    [Fact]
    public void CanBeCancelledWithoutPenalty_WithCustomWindow12Hours_EvaluatesCorrectly()
    {
        var startTime = new DateTime(2026, 10, 10, 14, 0, 0, DateTimeKind.Utc);
        var reservation = CreateValidReservation(startTime: startTime);
        var customWindow = TimeSpan.FromHours(12);

        reservation.CanBeCancelledWithoutPenalty(startTime.AddHours(-14), customWindow).Should().BeTrue();
        reservation.CanBeCancelledWithoutPenalty(startTime.AddHours(-10), customWindow).Should().BeFalse();
    }

    #endregion

    #region 5. Immutability & Concurrency Verification

    [Fact]
    public void Reservation_InstantiatesAndVerifiesAllInvariantsFailFast()
    {
        var reservationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var courtId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(2);

        var reservation = Reservation.Create(
            reservationId,
            customerId,
            "VIP",
            courtId,
            branchId,
            null,
            startTime,
            endTime,
            300000m,
            150000m,
            PaymentStatus.PARTIALLY_PAID,
            "SINGLE",
            BookingPriority.VIP,
            DateTime.UtcNow);

        reservation.Should().NotBeNull();
        reservation.ReservationId.Should().Be(reservationId);
        reservation.Customer.Tier.Should().Be("VIP");
        reservation.Priority.Should().Be(BookingPriority.VIP);
        reservation.Payment.DepositPercentage.Should().Be(50m);
        reservation.Schedule.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task Reservation_ConcurrentAccess_IsThreadSafeAndDeterministic()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddDays(2);
        var reservation = Reservation.Create(
            Guid.NewGuid(), Guid.NewGuid(), "GOLD", Guid.NewGuid(), Guid.NewGuid(), null,
            startTime, startTime.AddHours(2), 200000m, 100000m,
            PaymentStatus.PARTIALLY_PAID, "SINGLE", BookingPriority.NORMAL, DateTime.UtcNow);

        // Act: 100 luồng đồng thời gọi các hàm tính toán trên cùng 1 instance Reservation bất biến
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            var ratio = reservation.CalculateDepositRatio();
            var canCancel = reservation.CanBeCancelledWithoutPenalty(DateTime.UtcNow);

            ratio.Should().Be(0.5m);
            canCancel.Should().BeTrue();
        }));

        // Assert: Không xảy ra Race Condition hay Exception
        await Task.WhenAll(tasks);
    }

    #endregion

    #region Helper Methods

    private static Reservation CreateValidReservation(
        DateTime? startTime = null,
        decimal totalPrice = 200000m,
        decimal depositAmount = 100000m,
        string? bookingType = "SINGLE")
    {
        var start = startTime ?? DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(2);

        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "GOLD",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: start,
            endTime: end,
            totalPrice: totalPrice,
            depositAmount: depositAmount,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: bookingType,
            priority: BookingPriority.NORMAL,
            requestedAt: DateTime.UtcNow);
    }

    #endregion
}
