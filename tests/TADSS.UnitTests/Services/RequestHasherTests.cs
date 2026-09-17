using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.Runtime.Api.Services;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Services;

public class RequestHasherTests
{
    [Fact]
    public void ComputeHash_IdenticalRequests_ProduceIdentical64HexChars()
    {
        // Arrange
        var reservationId = Guid.NewGuid();
        var courtId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var request1 = new DecisionRequestDto
        {
            ReservationId = reservationId,
            CourtId = courtId,
            BranchId = branchId,
            CustomerId = customerId,
            CustomerTier = "Gold",
            StartTime = now,
            EndTime = now.AddHours(2),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 150000m,
            PaymentStatus = "PartiallyPaid",
            Priority = "Normal",
            RequestedAt = now
        };

        var request2 = new DecisionRequestDto
        {
            ReservationId = reservationId,
            CourtId = courtId,
            BranchId = branchId,
            CustomerId = customerId,
            CustomerTier = "gold", // case-insensitive test
            StartTime = now,
            EndTime = now.AddHours(2),
            BookingType = "single",
            TotalPrice = 300000.00m,
            DepositAmount = 150000.00m,
            PaymentStatus = "partiallypaid",
            Priority = "normal",
            RequestedAt = now
        };

        // Act
        var hash1 = RequestHasher.ComputeHash(request1);
        var hash2 = RequestHasher.ComputeHash(request2);

        // Assert
        hash1.Should().HaveLength(64);
        hash2.Should().HaveLength(64);
        hash1.Should().Be(hash2);
        hash1.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_DifferentPrice_ProducesDifferentHash()
    {
        // Arrange
        var baseRequest = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Gold",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 150000m,
            PaymentStatus = "PartiallyPaid",
            Priority = "Normal",
            RequestedAt = DateTime.UtcNow
        };

        var modifiedRequest = new DecisionRequestDto
        {
            ReservationId = baseRequest.ReservationId,
            CourtId = baseRequest.CourtId,
            BranchId = baseRequest.BranchId,
            CustomerId = baseRequest.CustomerId,
            CustomerTier = baseRequest.CustomerTier,
            StartTime = baseRequest.StartTime,
            EndTime = baseRequest.EndTime,
            BookingType = baseRequest.BookingType,
            TotalPrice = 500000m, // Changed
            DepositAmount = baseRequest.DepositAmount,
            PaymentStatus = baseRequest.PaymentStatus,
            Priority = baseRequest.Priority,
            RequestedAt = baseRequest.RequestedAt
        };

        // Act
        var hash1 = RequestHasher.ComputeHash(baseRequest);
        var hash2 = RequestHasher.ComputeHash(modifiedRequest);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_MetadataInsertionOrderDoesNotChangeCanonicalHash()
    {
        var first = DecisionRequestFactory.ValidDecisionRequest();
        first.Metadata = new Dictionary<string, string>
        {
            ["z-last"] = "value-2",
            ["a-first"] = "value-1"
        };

        var second = DecisionRequestFactory.ValidDecisionRequest();
        second.Metadata = new Dictionary<string, string>
        {
            ["a-first"] = "value-1",
            ["z-last"] = "value-2"
        };

        RequestHasher.ComputeHash(first).Should().Be(RequestHasher.ComputeHash(second));
    }

    [Fact]
    public void ComputeHash_MetadataValueChangeProducesDifferentHash()
    {
        var original = DecisionRequestFactory.ValidDecisionRequest();
        var changed = DecisionRequestFactory.ValidDecisionRequest();
        changed.Metadata!["tenant"] = "another-tenant";

        RequestHasher.ComputeHash(original).Should().NotBe(RequestHasher.ComputeHash(changed));
    }
}
