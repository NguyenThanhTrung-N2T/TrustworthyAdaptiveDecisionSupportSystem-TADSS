using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Contracts;

public class DecisionWireContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void DecisionRequestDto_Serialization_UsesStableWireNames()
    {
        var dto = DecisionRequestFactory.ValidDecisionRequest();

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string[] expectedProperties =
        [
            "reservationId", "courtId", "courtTypeId", "branchId", "customerId",
            "customerTier", "startTime", "endTime", "bookingType", "totalPrice",
            "depositAmount", "paymentStatus", "priority", "requestedAt", "metadata"
        ];

        foreach (var property in expectedProperties)
        {
            root.TryGetProperty(property, out _).Should().BeTrue(
                because: $"wire contract must expose '{property}'");
        }

        root.TryGetProperty("ReservationId", out _).Should().BeFalse();
        root.TryGetProperty("TotalPrice", out _).Should().BeFalse();
    }

    [Fact]
    public void DecisionRequestDto_Serialization_PreservesDecimalPrecision()
    {
        var dto = DecisionRequestFactory.ValidDecisionRequest();
        dto.TotalPrice = 123456.7891m;
        dto.DepositAmount = 0.0001m;

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("totalPrice").GetDecimal().Should().Be(123456.7891m);
        root.GetProperty("depositAmount").GetDecimal().Should().Be(0.0001m);
    }

    [Fact]
    public void DecisionRequestDto_Serialization_WithNullOptionalFields_EmitsNullValues()
    {
        var dto = DecisionRequestFactory.ValidDecisionRequest();
        dto.CourtTypeId = null;
        dto.CustomerTier = null;
        dto.Priority = null;
        dto.Metadata = null;

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("courtTypeId").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("metadata").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void DecisionResultDto_RoundTripSerialization_PreservesFullStructure()
    {
        var dto = DecisionRequestFactory.ValidDecisionResult();

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DecisionResultDto>(json, JsonOptions);

        deserialized.Should().BeEquivalentTo(dto);
        deserialized!.Recommendations.Should().ContainSingle()
            .Which.Type.Should().Be("REQUIRE_DEPOSIT");
    }

    [Fact]
    public void DecisionRecommendationDto_RoundTripSerialization_PreservesNullSuggestionFields()
    {
        var dto = new DecisionRecommendationDto
        {
            Type = "ALTERNATIVE_SLOT",
            Description = "Đổi sang khung giờ 20:00–22:00",
            SuggestedCourtId = null,
            SuggestedStartTime = null,
            SuggestedEndTime = null,
            SuggestedPrice = null,
            ConfidenceScore = 0.95
        };

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DecisionRecommendationDto>(json, JsonOptions);

        deserialized.Should().BeEquivalentTo(dto);
        deserialized!.ConfidenceScore.Should().Be(0.95);
    }
}
