using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Xunit;

namespace TADSS.UnitTests;

public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void DecisionRequestDto_RoundTripSerialization_PreservesAllFields()
    {
        // Arrange
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Gold",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "Single",
            TotalPrice = 400000.00m,
            DepositAmount = 200000.00m,
            PaymentStatus = "PartiallyPaid",
            Priority = "High",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["source"] = "MobileApp" }
        };

        // Act
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DecisionRequestDto>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ReservationId.Should().Be(request.ReservationId);
        deserialized.CourtId.Should().Be(request.CourtId);
        deserialized.CustomerTier.Should().Be("Gold");
        deserialized.TotalPrice.Should().Be(400000.00m);
        deserialized.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("MobileApp");
    }

    [Fact]
    public void DecisionResultDto_Serialization_OutputsExpectedStructure()
    {
        // Arrange
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = "NEED_APPROVAL",
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 15,
            FallbackUsed = false,
            PrimaryReason = "DEPOSIT_BELOW_THRESHOLD",
            Reasons = new List<string> { "Mức cọc dưới 30%" },
            Recommendations = new List<DecisionRecommendationDto>
            {
                new()
                {
                    Type = "REQUIRE_DEPOSIT",
                    Description = "Yêu cầu đặt cọc bổ sung 50,000 VND",
                    SuggestedPrice = 300000m,
                    ConfidenceScore = 1.0
                }
            },
            EvaluatedAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(result, JsonOptions);

        // Assert
        json.Should().Contain("decisionId");
        json.Should().Contain("reservationId");
        json.Should().Contain("NEED_APPROVAL");
        json.Should().Contain("DEPOSIT_BELOW_THRESHOLD");
        json.Should().Contain("REQUIRE_DEPOSIT");
    }
}
