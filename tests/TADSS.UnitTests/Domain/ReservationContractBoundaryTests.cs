using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Domain;

public class ReservationContractBoundaryTests
{
    [Fact]
    public void Create_WithDefaultOptionalValues_UsesStandardDefaults()
    {
        var reservation = Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: null,
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            endTime: new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
            totalPrice: 300000m,
            depositAmount: 0m,
            paymentStatus: PaymentStatus.UNPAID,
            bookingType: null,
            priority: BookingPriority.NORMAL,
            requestedAt: new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc));

        reservation.Customer.Tier.Should().Be("STANDARD");
        reservation.BookingType.Should().Be("SINGLE");
        reservation.Resource.CourtTypeId.Should().BeNull();
        reservation.Metadata.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithWhitespaceValues_TrimsBeforeApplyingDefaults()
    {
        var reservation = Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "  GOLD  ",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            endTime: new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
            totalPrice: 300000m,
            depositAmount: 150000m,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: "  RECURRING  ",
            priority: BookingPriority.HIGH,
            requestedAt: new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc));

        reservation.Customer.Tier.Should().Be("GOLD");
        reservation.BookingType.Should().Be("RECURRING");
        reservation.Schedule.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [InlineData("", "SINGLE")]
    [InlineData("   ", "SINGLE")]
    [InlineData(null, "SINGLE")]
    public void Create_WithEmptyBookingType_DefaultsToSingle(string? bookingType, string expected)
    {
        var reservation = Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "GOLD",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            endTime: new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
            totalPrice: 300000m,
            depositAmount: 150000m,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: bookingType,
            priority: BookingPriority.NORMAL,
            requestedAt: new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc));

        reservation.BookingType.Should().Be(expected);
    }
}
