using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Contracts;

/// <summary>
/// Bộ kiểm thử tương thích Hợp đồng (Contract Compatibility Tests), chuyển đổi 2 chiều và tính ổn định của Serializer.
/// </summary>
public class ContractSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region 1. DTO Serialization & Wire Compatibility

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
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "SINGLE",
            TotalPrice = 400000.00m,
            DepositAmount = 200000.00m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "HIGH",
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
        deserialized.CustomerTier.Should().Be("GOLD");
        deserialized.TotalPrice.Should().Be(400000.00m);
        deserialized.PaymentStatus.Should().Be("PARTIALLY_PAID");
        deserialized.Priority.Should().Be("HIGH");
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
            Reasons = ["Mức cọc dưới 30%"],
            Recommendations =
            [
                new DecisionRecommendationDto
                {
                    Type = "REQUIRE_DEPOSIT",
                    Description = "Yêu cầu đặt cọc bổ sung 50,000 VND",
                    SuggestedPrice = 300000m,
                    ConfidenceScore = 1.0
                }
            ],
            EvaluatedAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(result, JsonOptions);

        // Assert
        json.Should().Contain("\"decisionId\"");
        json.Should().Contain("\"reservationId\"");
        json.Should().Contain("\"NEED_APPROVAL\"");
        json.Should().Contain("\"DEPOSIT_BELOW_THRESHOLD\"");
        json.Should().Contain("\"REQUIRE_DEPOSIT\"");
    }

    #endregion

    #region 2. Mapping Boundary Tests (DTO <-> Domain)

    [Fact]
    public void DecisionRequestDto_ToDomain_MapsAllPropertiesCorrectly()
    {
        // Arrange
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CourtTypeId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            TotalPrice = 500000m,
            DepositAmount = 250000m,
            PaymentStatus = "PARTIALLY_PAID",
            BookingType = "RECURRING",
            Priority = "URGENT",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["channel"] = "WebPortal" }
        };

        // Act
        var domain = dto.ToDomain();

        // Assert
        domain.ReservationId.Should().Be(dto.ReservationId);
        domain.Customer.CustomerId.Should().Be(dto.CustomerId);
        domain.Customer.Tier.Should().Be("VIP");
        domain.Resource.CourtId.Should().Be(dto.CourtId);
        domain.Resource.BranchId.Should().Be(dto.BranchId);
        domain.Priority.Should().Be(BookingPriority.URGENT);
        domain.Payment.PaymentStatus.Should().Be(PaymentStatus.PARTIALLY_PAID);
        domain.Metadata.Get("channel").Should().Be("WebPortal");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DecisionRequestDto_ToDomain_WithNullOrEmptyPriority_DefaultsToNormal(string? emptyPriority)
    {
        // Arrange
        var dto = CreateValidDto();
        dto.Priority = emptyPriority;

        // Act
        var domain = dto.ToDomain();

        // Assert
        domain.Priority.Should().Be(BookingPriority.NORMAL);
    }

    [Theory]
    [InlineData("SUPER_VIP")]
    [InlineData("INVALID_VALUE")]
    [InlineData("EMERGENCY")]
    public void DecisionRequestDto_ToDomain_WithInvalidExplicitPriority_ThrowsContractValidationException(string invalidPriority)
    {
        // Arrange
        var dto = CreateValidDto();
        dto.Priority = invalidPriority;

        // Act
        var act = () => dto.ToDomain();

        // Assert: Không âm thầm fallback, mà ném lỗi vi phạm hợp đồng tường minh
        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_BOOKING_PRIORITY");
    }

    [Theory]
    [InlineData("UNKNOWN_PAYMENT")]
    [InlineData("NOT_EXISTING")]
    public void DecisionRequestDto_ToDomain_WithInvalidPaymentStatus_ThrowsContractValidationException(string invalidPaymentStatus)
    {
        // Arrange
        var dto = CreateValidDto();
        dto.PaymentStatus = invalidPaymentStatus;

        // Act
        var act = () => dto.ToDomain();

        // Assert
        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_PAYMENT_STATUS");
    }

    [Fact]
    public void BidirectionalMapping_DomainToDtoToDomain_PreservesSemanticEquivalence()
    {
        // Arrange
        var initialDto = CreateValidDto();
        initialDto.Priority = "VIP";
        initialDto.PaymentStatus = "PAID";
        initialDto.DepositAmount = initialDto.TotalPrice;

        // Act: DTO -> Domain -> DTO -> Domain
        var domain1 = initialDto.ToDomain();
        var convertedDto = domain1.ToDto();
        var domain2 = convertedDto.ToDomain();

        // Assert
        domain2.ReservationId.Should().Be(domain1.ReservationId);
        domain2.Customer.CustomerId.Should().Be(domain1.Customer.CustomerId);
        domain2.Customer.Tier.Should().Be(domain1.Customer.Tier);
        domain2.Priority.Should().Be(domain1.Priority);
        domain2.Payment.PaymentStatus.Should().Be(domain1.Payment.PaymentStatus);
        domain2.Payment.TotalPrice.Should().Be(domain1.Payment.TotalPrice);
        domain2.Payment.DepositAmount.Should().Be(domain1.Payment.DepositAmount);
        convertedDto.Priority.Should().Be("VIP");
        convertedDto.PaymentStatus.Should().Be("PAID");
    }

    #endregion

    #region 3. Enum Stability Tests

    [Fact]
    public void BookingPriority_EnumNumericValuesAreLocked()
    {
        ((int)BookingPriority.NORMAL).Should().Be(0);
        ((int)BookingPriority.HIGH).Should().Be(1);
        ((int)BookingPriority.VIP).Should().Be(2);
        ((int)BookingPriority.URGENT).Should().Be(3);
    }

    #endregion

    #region Helpers

    private static DecisionRequestDto CreateValidDto()
    {
        return new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "STANDARD",
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CourtTypeId = null,
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            BookingType = "SINGLE",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["source"] = "UnitTest" }
        };
    }

    #endregion
}
