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

    [Fact]
    public void Constructor_NullRequiredObjects_ThrowsArgumentNullException()
    {
        var validCustomer = new CustomerInfo(Guid.NewGuid(), "GOLD");
        var validResource = new ResourceInfo(Guid.NewGuid(), Guid.NewGuid());
        var validSchedule = new ReservationSchedule(DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(3));
        var validPayment = new PaymentPolicy(200000m, 100000m, PaymentStatus.PARTIALLY_PAID);

        // Null Customer
        var act1 = () => new Reservation(Guid.NewGuid(), null!, validResource, validSchedule, validPayment, "SINGLE", BookingPriority.NORMAL, DateTime.UtcNow);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("customer");

        // Null Resource
        var act2 = () => new Reservation(Guid.NewGuid(), validCustomer, null!, validSchedule, validPayment, "SINGLE", BookingPriority.NORMAL, DateTime.UtcNow);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("resource");

        // Null Schedule
        var act3 = () => new Reservation(Guid.NewGuid(), validCustomer, validResource, null!, validPayment, "SINGLE", BookingPriority.NORMAL, DateTime.UtcNow);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("schedule");

        // Null Payment
        var act4 = () => new Reservation(Guid.NewGuid(), validCustomer, validResource, validSchedule, null!, "SINGLE", BookingPriority.NORMAL, DateTime.UtcNow);
        act4.Should().Throw<ArgumentNullException>().WithParameterName("payment");
    }

    [Fact]
    public void ValueObjects_SupportRecordValueEqualityAndCloning()
    {
        var custId = Guid.NewGuid();
        var c1 = new CustomerInfo(custId, "VIP");
        var c2 = new CustomerInfo(custId, "VIP");
        var c3 = c1 with { Tier = "GOLD" };

        c1.Should().Be(c2);
        c1.Should().NotBe(c3);
        c1.ToString().Should().Contain("VIP");

        var courtId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        var r1 = new ResourceInfo(courtId, branchId, typeId);
        var r2 = new ResourceInfo(courtId, branchId, typeId);
        var r3 = r1 with { CourtTypeId = null };

        r1.Should().Be(r2);
        r1.Should().NotBe(r3);
        r1.ToString().Should().Contain(courtId.ToString());

        var start = DateTime.UtcNow.AddHours(1);
        var end = start.AddHours(2);
        var s1 = new ReservationSchedule(start, end);
        var s2 = new ReservationSchedule(start, end);
        var s3 = s1 with { EndTime = start.AddHours(3) };

        s1.Should().Be(s2);
        s1.Should().NotBe(s3);
        s1.ToString().Should().Contain("ReservationSchedule").And.Contain("Duration");

        var p1 = new PaymentPolicy(200000m, 100000m, PaymentStatus.PARTIALLY_PAID);
        var p2 = new PaymentPolicy(200000m, 100000m, PaymentStatus.PARTIALLY_PAID);
        var p3 = p1 with { DepositAmount = 200000m, PaymentStatus = PaymentStatus.PAID };

        p1.Should().Be(p2);
        p1.Should().NotBe(p3);
        p1.ToString().Should().Contain("200000");

        var m1 = new ReservationMetadata(new Dictionary<string, string> { ["source"] = "Mobile" });
        var m2 = new ReservationMetadata(new Dictionary<string, string> { ["source"] = "Mobile" });
        var m3 = new ReservationMetadata(null);
        var mCloned = m1 with { };

        m1.Attributes.Should().BeEquivalentTo(m2.Attributes);
        mCloned.Attributes.Should().BeEquivalentTo(m1.Attributes);
        m3.Attributes.Should().BeEmpty();
        m1.Get("source").Should().Be("Mobile");
        m1.Get("nonexistent").Should().BeNull();
        m1.ToString().Should().Contain("ReservationMetadata");
    }

    [Fact]
    public void DomainInvariantException_Properties_AreAccuratelySet()
    {
        var ex = new DomainInvariantException("ERR_CODE", "Error message detail", "TargetField");
        ex.InvariantCode.Should().Be("ERR_CODE");
        ex.Message.Should().Be("Error message detail");
        ex.FieldName.Should().Be("TargetField");
    }
}

