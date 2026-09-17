using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Domain;

public class ConcurrentReadSafetyTests
{
    [Fact]
    public async Task ReservationAndMetadata_ConcurrentReadsRemainConsistent()
    {
        var reservation = Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "GOLD",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: Guid.NewGuid(),
            startTime: new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            endTime: new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
            totalPrice: 300000.50m,
            depositAmount: 150000.25m,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: "SINGLE",
            priority: BookingPriority.HIGH,
            requestedAt: new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc),
            metadata: new Dictionary<string, string>
            {
                ["source"] = "MobileApp",
                ["tenant"] = "smashcourt"
            });

        var reads = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                reservation.EnsureValid();
                reservation.Metadata.Get("source").Should().Be("MobileApp");
                reservation.Metadata.Get("tenant").Should().Be("smashcourt");
                reservation.Metadata.Get("missing").Should().BeNull();
            }

            return new
            {
                reservation.ReservationId,
                reservation.BookingType,
                reservation.Schedule.Duration,
                reservation.Payment.DepositPercentage,
                Source = reservation.Metadata.Get("source")
            };
        })).ToArray();

        var snapshots = await Task.WhenAll(reads);

        snapshots.Should().OnlyContain(snapshot =>
            snapshot.ReservationId == reservation.ReservationId
            && snapshot.BookingType == "SINGLE"
            && snapshot.Duration == TimeSpan.FromHours(2)
            && snapshot.DepositPercentage == 50m
            && snapshot.Source == "MobileApp");
    }
}
