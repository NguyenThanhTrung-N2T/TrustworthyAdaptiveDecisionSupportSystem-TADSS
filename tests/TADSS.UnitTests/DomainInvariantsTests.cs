using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests;

public class DomainInvariantsTests
{
    [Fact]
    public void ReservationSchedule_EndTimeBeforeStartTime_ThrowsDomainInvariantException()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = DateTime.UtcNow.AddHours(1);

        // Act
        var act = () => new ReservationSchedule(startTime, endTime);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_RANGE");
    }

    [Fact]
    public void ReservationSchedule_DurationLessThan30Minutes_ThrowsDomainInvariantException()
    {
        // Arrange
        var startTime = DateTime.UtcNow.AddHours(2);
        var endTime = startTime.AddMinutes(15);

        // Act
        var act = () => new ReservationSchedule(startTime, endTime);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    [Fact]
    public void PaymentPolicy_DepositExceedsTotalPrice_ThrowsDomainInvariantException()
    {
        // Arrange
        var totalPrice = 200000m;
        var depositAmount = 300000m;

        // Act
        var act = () => new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.PARTIALLY_PAID);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Fact]
    public void PaymentPolicy_NegativePrice_ThrowsDomainInvariantException()
    {
        // Arrange & Act
        var act = () => new PaymentPolicy(-1000m, 0m, PaymentStatus.UNPAID);

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_TOTAL_PRICE");
    }

    [Fact]
    public void CustomerInfo_EmptyCustomerId_ThrowsDomainInvariantException()
    {
        // Arrange & Act
        var act = () => new CustomerInfo(Guid.Empty, "STANDARD");

        // Assert
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_CUSTOMER_ID");
    }

    [Fact]
    public void Reservation_ValidParameters_InstantiatesSuccessfully()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var courtId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var startTime = DateTime.UtcNow.AddDays(1);
        var endTime = startTime.AddHours(2);

        // Act
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
            "NORMAL",
            DateTime.UtcNow);

        // Assert
        reservation.Should().NotBeNull();
        reservation.ReservationId.Should().Be(reservationId);
        reservation.Customer.Tier.Should().Be("VIP");
        reservation.Payment.DepositPercentage.Should().Be(50m);
        reservation.Schedule.Duration.Should().Be(TimeSpan.FromHours(2));
    }
}
