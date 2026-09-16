using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests;

/// <summary>
/// Bộ kiểm thử tương thích Hợp đồng và ánh xạ nâng cao đối kháng (Adversarial Contract Mapping & Casing Fuzzing Tests).
/// 
/// NGUYÊN TẮC KIẾN TRÚC:
/// 1. Wire Canonical Specification: Chuẩn hóa 100% là SCREAMING_SNAKE_CASE (như đã định nghĩa trong docs/contracts/schemas/).
/// 2. Ingestion Boundary Normalization (Option B): Áp dụng Định luật Postel (Robustness Principle) tại cổng tiếp nhận
///    (cho phép chuẩn hóa các alias casing như camelCase, PascalCase, kebab-case sang SCREAMING_SNAKE_CASE),
///    đồng thời bắt buộc kiểm tra Enum.IsDefined để chặn đứng lỗ hổng ép kiểu số nguyên ("12345").
/// 3. Closed-World Schema vs Ingestion Resilience: Schema JSON Gateway áp dụng "additionalProperties: false" để bảo vệ ranh giới mạng,
///    trong khi DTO Serializer được cấu hình linh hoạt phục vụ tiến hóa mã nguồn.
/// </summary>
public class AdversarialContractMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region 1. Enum Ingestion Normalization & Resilient Casing Aliases

    [Theory]
    [InlineData("normal", BookingPriority.NORMAL)]
    [InlineData("Normal", BookingPriority.NORMAL)]
    [InlineData("NORMAL", BookingPriority.NORMAL)]
    [InlineData("high", BookingPriority.HIGH)]
    [InlineData("High", BookingPriority.HIGH)]
    [InlineData("HIGH", BookingPriority.HIGH)]
    [InlineData("vip", BookingPriority.VIP)]
    [InlineData("Vip", BookingPriority.VIP)]
    [InlineData("VIP", BookingPriority.VIP)]
    [InlineData("urgent", BookingPriority.URGENT)]
    [InlineData("Urgent", BookingPriority.URGENT)]
    [InlineData("URGENT", BookingPriority.URGENT)]
    [InlineData("  urgent  ", BookingPriority.URGENT)]
    public void DecisionMappingExtensions_ParseBookingPriority_ParsesAllCasingVariations(string rawInput, BookingPriority expected)
    {
        var result = DecisionMappingExtensions.ParseBookingPriority(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("unpaid", PaymentStatus.UNPAID)]
    [InlineData("Unpaid", PaymentStatus.UNPAID)]
    [InlineData("UNPAID", PaymentStatus.UNPAID)]
    [InlineData("partiallyPaid", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("PartiallyPaid", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("partially_paid", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("partially-paid", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("PARTIALLY_PAID", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("paid", PaymentStatus.PAID)]
    [InlineData("Paid", PaymentStatus.PAID)]
    [InlineData("PAID", PaymentStatus.PAID)]
    [InlineData("refunded", PaymentStatus.REFUNDED)]
    [InlineData("Refunded", PaymentStatus.REFUNDED)]
    [InlineData("REFUNDED", PaymentStatus.REFUNDED)]
    public void DecisionMappingExtensions_ParsePaymentStatus_ParsesAllCasingAndDelimiters(string rawInput, PaymentStatus expected)
    {
        var result = DecisionMappingExtensions.ParsePaymentStatus(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("approved", DecisionStatus.APPROVED)]
    [InlineData("Approved", DecisionStatus.APPROVED)]
    [InlineData("APPROVED", DecisionStatus.APPROVED)]
    [InlineData("needApproval", DecisionStatus.NEED_APPROVAL)]
    [InlineData("NeedApproval", DecisionStatus.NEED_APPROVAL)]
    [InlineData("need_approval", DecisionStatus.NEED_APPROVAL)]
    [InlineData("need-approval", DecisionStatus.NEED_APPROVAL)]
    [InlineData("NEED_APPROVAL", DecisionStatus.NEED_APPROVAL)]
    [InlineData("rejected", DecisionStatus.REJECTED)]
    [InlineData("Rejected", DecisionStatus.REJECTED)]
    [InlineData("REJECTED", DecisionStatus.REJECTED)]
    public void DecisionMappingExtensions_ParseDecisionStatus_ParsesAllCasingAndDelimiters(string rawInput, DecisionStatus expected)
    {
        var result = DecisionMappingExtensions.ParseDecisionStatus(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("alternativeSlot", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("AlternativeSlot", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("alternative_slot", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("alternative-slot", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("ALTERNATIVE_SLOT", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("alternativeCourt", RecommendationType.ALTERNATIVE_COURT)]
    [InlineData("AlternativeCourt", RecommendationType.ALTERNATIVE_COURT)]
    [InlineData("alternative_court", RecommendationType.ALTERNATIVE_COURT)]
    [InlineData("requireDeposit", RecommendationType.REQUIRE_DEPOSIT)]
    [InlineData("RequireDeposit", RecommendationType.REQUIRE_DEPOSIT)]
    [InlineData("require_deposit", RecommendationType.REQUIRE_DEPOSIT)]
    [InlineData("priceAdjustment", RecommendationType.PRICE_ADJUSTMENT)]
    [InlineData("PriceAdjustment", RecommendationType.PRICE_ADJUSTMENT)]
    [InlineData("price_adjustment", RecommendationType.PRICE_ADJUSTMENT)]
    [InlineData("requireApproval", RecommendationType.REQUIRE_APPROVAL)]
    [InlineData("priorityEscalation", RecommendationType.PRIORITY_ESCALATION)]
    public void DecisionMappingExtensions_ParseRecommendationType_ParsesAllCasingAndDelimiters(string rawInput, RecommendationType expected)
    {
        var result = DecisionMappingExtensions.ParseRecommendationType(rawInput);
        result.Should().Be(expected);
    }

    #endregion

    #region 2. Negative Tests for Invalid Enum Payloads

    [Theory]
    [InlineData("SUPER_VIP")]
    [InlineData("INVALID_PRIORITY")]
    [InlineData("12345")]
    public void DecisionMappingExtensions_InvalidBookingPriority_ThrowsContractValidationException(string invalidPriority)
    {
        var act = () => DecisionMappingExtensions.ParseBookingPriority(invalidPriority);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_BOOKING_PRIORITY");
    }

    [Theory]
    [InlineData("PENDING_PAYMENT")]
    [InlineData("UNKNOWN_STATUS")]
    [InlineData("VOIDED")]
    public void DecisionMappingExtensions_InvalidPaymentStatus_ThrowsContractValidationException(string invalidStatus)
    {
        var act = () => DecisionMappingExtensions.ParsePaymentStatus(invalidStatus);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_PAYMENT_STATUS");
    }

    [Theory]
    [InlineData("MAYBE")]
    [InlineData("PENDING")]
    [InlineData("HOLD")]
    public void DecisionMappingExtensions_InvalidDecisionStatus_ThrowsContractValidationException(string invalidStatus)
    {
        var act = () => DecisionMappingExtensions.ParseDecisionStatus(invalidStatus);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_DECISION_STATUS");
    }

    [Theory]
    [InlineData("NON_EXISTENT_RECOMMENDATION")]
    [InlineData("UNKNOWN_REC")]
    public void DecisionMappingExtensions_InvalidRecommendationType_ThrowsContractValidationException(string invalidType)
    {
        var act = () => DecisionMappingExtensions.ParseRecommendationType(invalidType);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_RECOMMENDATION_TYPE");
    }

    #endregion

    #region 3. Lossless 2-Way Roundtrip Mapping (DTO <-> Domain)

    [Fact]
    public void DecisionMappingExtensions_TwoWayRoundtrip_PreservesAllFieldsExactly()
    {
        // Arrange
        var originalDto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "DIAMOND",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "RECURRING",
            TotalPrice = 500000m,
            DepositAmount = 250000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "URGENT",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "AdminPortal",
                ["vipBadge"] = "True"
            }
        };

        // Act: DTO -> Domain Entity -> DTO
        var domain = originalDto.ToDomain();
        var roundtrippedDto = domain.ToDto();

        // Assert
        roundtrippedDto.ReservationId.Should().Be(originalDto.ReservationId);
        roundtrippedDto.CourtId.Should().Be(originalDto.CourtId);
        roundtrippedDto.CourtTypeId.Should().Be(originalDto.CourtTypeId);
        roundtrippedDto.BranchId.Should().Be(originalDto.BranchId);
        roundtrippedDto.CustomerId.Should().Be(originalDto.CustomerId);
        roundtrippedDto.CustomerTier.Should().Be(originalDto.CustomerTier);
        roundtrippedDto.StartTime.Should().Be(originalDto.StartTime);
        roundtrippedDto.EndTime.Should().Be(originalDto.EndTime);
        roundtrippedDto.BookingType.Should().Be(originalDto.BookingType);
        roundtrippedDto.TotalPrice.Should().Be(originalDto.TotalPrice);
        roundtrippedDto.DepositAmount.Should().Be(originalDto.DepositAmount);
        roundtrippedDto.PaymentStatus.Should().Be("PARTIALLY_PAID");
        roundtrippedDto.Priority.Should().Be("URGENT");
        roundtrippedDto.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("AdminPortal");
        roundtrippedDto.Metadata.Should().ContainKey("vipBadge").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void DecisionMappingExtensions_NullCourtTypeId_RoundtripsSuccessfully()
    {
        // Arrange
        var originalDto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = null,
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "STANDARD",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            BookingType = "SINGLE",
            TotalPrice = 100000m,
            DepositAmount = 50000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var domain = originalDto.ToDomain();
        var roundtrippedDto = domain.ToDto();

        // Assert
        roundtrippedDto.CourtTypeId.Should().BeNull();
    }

    #endregion

    #region 4. Extreme JSON Payloads & Forward Compatibility

    [Fact]
    public void DecisionRequestDto_Deserialization_IgnoresUnknownExtraFieldsGracefully()
    {
        // Arrange: Wire payload chứa các trường mở rộng không có trong DTO (Forward Compatibility)
        var jsonWithUnknownFields = """
        {
            "reservationId": "d3b07384-d113-4c54-9494-0f2a7a40b955",
            "courtId": "e3b07384-d113-4c54-9494-0f2a7a40b956",
            "branchId": "f3b07384-d113-4c54-9494-0f2a7a40b957",
            "customerId": "03b07384-d113-4c54-9494-0f2a7a40b958",
            "customerTier": "VIP",
            "startTime": "2026-10-15T08:00:00Z",
            "endTime": "2026-10-15T10:00:00Z",
            "bookingType": "SINGLE",
            "totalPrice": 200000.0,
            "depositAmount": 100000.0,
            "paymentStatus": "PARTIALLY_PAID",
            "priority": "HIGH",
            "requestedAt": "2026-10-14T08:00:00Z",
            "futureFieldV3": "experimentalValue",
            "extraTracingHeader": 12345
        }
        """;

        // Act
        var deserialized = JsonSerializer.Deserialize<DecisionRequestDto>(jsonWithUnknownFields, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.ReservationId.Should().Be(Guid.Parse("d3b07384-d113-4c54-9494-0f2a7a40b955"));
        deserialized.CustomerTier.Should().Be("VIP");
        deserialized.TotalPrice.Should().Be(200000.0m);
    }

    [Fact]
    public void DecisionResultDto_EmptyRecommendationsAndReasons_SerializesToCleanJson()
    {
        // Arrange
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.APPROVED.ToString(),
            StrategyName = "FastTrackStrategy",
            StrategyVersion = "1.0.0",
            ExecutionTimeMs = 3,
            FallbackUsed = false,
            PrimaryReason = ErrorCodes.ApprovedStandard,
            Reasons = [],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        // Act
        var json = JsonSerializer.Serialize(result, JsonOptions);

        // Assert
        json.Should().Contain("\"reasons\":[]");
        json.Should().Contain("\"recommendations\":[]");
        json.Should().Contain("\"APPROVED\"");
    }

    #endregion
}
