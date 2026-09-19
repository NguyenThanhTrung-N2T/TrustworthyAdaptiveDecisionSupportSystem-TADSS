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
/// Bộ kiểm thử tương thích Hợp đồng và ánh xạ nâng cao đối kháng (Adversarial Contract Mapping Tests).
/// 
/// NGUYÊN TẮC KIẾN TRÚC (OPTION A - STRICT CANONICAL ONLY):
/// 1. Wire Canonical Specification: Chuẩn hóa 100% là SCREAMING_SNAKE_CASE (như đã định nghĩa trong docs/contracts/schemas/).
/// 2. Strict Ingestion Boundary: Bắt buộc giá trị enum trên wire phải khớp chính xác SCREAMING_SNAKE_CASE (case-sensitive).
///    Mọi biến thể casing (camelCase, PascalCase, kebab-case, snake_case), chuỗi số nguyên ("0", "12345") hoặc giá trị lạ
///    đều bị từ chối dứt khoát bằng ContractValidationException.
/// 3. Roundtrip Fidelity: Đảm bảo dữ liệu chuyển đổi 2 chiều DTO <-> Domain không bao giờ bị biến dạng hoặc suy hao thông tin.
/// </summary>
public class AdversarialContractMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    #region 1. Strict Canonical SCREAMING_SNAKE_CASE Parsing

    [Theory]
    [InlineData("NORMAL", BookingPriority.NORMAL)]
    [InlineData("HIGH", BookingPriority.HIGH)]
    [InlineData("VIP", BookingPriority.VIP)]
    [InlineData("URGENT", BookingPriority.URGENT)]
    [InlineData("  URGENT  ", BookingPriority.URGENT)]
    [InlineData("", BookingPriority.NORMAL)]
    [InlineData("   ", BookingPriority.NORMAL)]
    [InlineData(null, BookingPriority.NORMAL)]
    public void DecisionMappingExtensions_ParseBookingPriority_ParsesCanonicalAndDefaults(string? rawInput, BookingPriority expected)
    {
        var result = DecisionMappingExtensions.ParseBookingPriority(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("UNPAID", PaymentStatus.UNPAID)]
    [InlineData("PARTIALLY_PAID", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("PAID", PaymentStatus.PAID)]
    [InlineData("REFUNDED", PaymentStatus.REFUNDED)]
    [InlineData("EXPIRED", PaymentStatus.EXPIRED)]
    [InlineData("  PARTIALLY_PAID  ", PaymentStatus.PARTIALLY_PAID)]
    [InlineData("", PaymentStatus.UNPAID)]
    [InlineData("   ", PaymentStatus.UNPAID)]
    [InlineData(null, PaymentStatus.UNPAID)]
    public void DecisionMappingExtensions_ParsePaymentStatus_ParsesCanonicalAndDefaults(string? rawInput, PaymentStatus expected)
    {
        var result = DecisionMappingExtensions.ParsePaymentStatus(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("APPROVED", DecisionStatus.APPROVED)]
    [InlineData("REJECTED", DecisionStatus.REJECTED)]
    [InlineData("NEED_APPROVAL", DecisionStatus.NEED_APPROVAL)]
    [InlineData("  APPROVED  ", DecisionStatus.APPROVED)]
    [InlineData("", DecisionStatus.NEED_APPROVAL)]
    [InlineData("   ", DecisionStatus.NEED_APPROVAL)]
    [InlineData(null, DecisionStatus.NEED_APPROVAL)]
    public void DecisionMappingExtensions_ParseDecisionStatus_ParsesCanonicalAndDefaults(string? rawInput, DecisionStatus expected)
    {
        var result = DecisionMappingExtensions.ParseDecisionStatus(rawInput);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("ALTERNATIVE_SLOT", RecommendationType.ALTERNATIVE_SLOT)]
    [InlineData("ALTERNATIVE_COURT", RecommendationType.ALTERNATIVE_COURT)]
    [InlineData("REQUIRE_DEPOSIT", RecommendationType.REQUIRE_DEPOSIT)]
    [InlineData("PRICE_ADJUSTMENT", RecommendationType.PRICE_ADJUSTMENT)]
    [InlineData("REQUIRE_APPROVAL", RecommendationType.REQUIRE_APPROVAL)]
    [InlineData("PRIORITY_ESCALATION", RecommendationType.PRIORITY_ESCALATION)]
    [InlineData("  ALTERNATIVE_SLOT  ", RecommendationType.ALTERNATIVE_SLOT)]
    public void DecisionMappingExtensions_ParseRecommendationType_ParsesCanonical(string rawInput, RecommendationType expected)
    {
        var result = DecisionMappingExtensions.ParseRecommendationType(rawInput);
        result.Should().Be(expected);
    }

    #endregion

    #region 2. Negative Tests for Non-Canonical Casing, Numeric Strings, and Invalid Enums

    [Theory]
    [InlineData("normal")]
    [InlineData("Normal")]
    [InlineData("high")]
    [InlineData("High")]
    [InlineData("vip")]
    [InlineData("Vip")]
    [InlineData("urgent")]
    [InlineData("Urgent")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("-1")]
    [InlineData("SUPER_VIP")]
    [InlineData("INVALID_PRIORITY")]
    public void DecisionMappingExtensions_NonCanonicalBookingPriority_ThrowsContractValidationException(string invalidPriority)
    {
        var act = () => DecisionMappingExtensions.ParseBookingPriority(invalidPriority);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_BOOKING_PRIORITY");
    }

    [Theory]
    [InlineData("unpaid")]
    [InlineData("Unpaid")]
    [InlineData("partiallyPaid")]
    [InlineData("PartiallyPaid")]
    [InlineData("partially_paid")]
    [InlineData("partially-paid")]
    [InlineData("paid")]
    [InlineData("Paid")]
    [InlineData("refunded")]
    [InlineData("Refunded")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("12345")]
    [InlineData("-1")]
    [InlineData("PENDING_PAYMENT")]
    [InlineData("UNKNOWN_STATUS")]
    [InlineData("VOIDED")]
    public void DecisionMappingExtensions_NonCanonicalPaymentStatus_ThrowsContractValidationException(string invalidStatus)
    {
        var act = () => DecisionMappingExtensions.ParsePaymentStatus(invalidStatus);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_PAYMENT_STATUS");
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("Approved")]
    [InlineData("needApproval")]
    [InlineData("NeedApproval")]
    [InlineData("need_approval")]
    [InlineData("need-approval")]
    [InlineData("rejected")]
    [InlineData("Rejected")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("MAYBE")]
    [InlineData("PENDING")]
    [InlineData("HOLD")]
    public void DecisionMappingExtensions_NonCanonicalDecisionStatus_ThrowsContractValidationException(string invalidStatus)
    {
        var act = () => DecisionMappingExtensions.ParseDecisionStatus(invalidStatus);

        act.Should().Throw<ContractValidationException>()
            .Where(e => e.ErrorCode == "INVALID_DECISION_STATUS");
    }

    [Theory]
    [InlineData("alternativeSlot")]
    [InlineData("AlternativeSlot")]
    [InlineData("alternative_slot")]
    [InlineData("alternative-slot")]
    [InlineData("alternativeCourt")]
    [InlineData("requireDeposit")]
    [InlineData("priceAdjustment")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("999")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NON_EXISTENT_RECOMMENDATION")]
    [InlineData("UNKNOWN_REC")]
    public void DecisionMappingExtensions_NonCanonicalRecommendationType_ThrowsContractValidationException(string? invalidType)
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

    [Fact]
    public void DecisionResultDto_WithFutureOrCustomReasonCode_SerializesAndDeserializesSuccessfully()
    {
        // Arrange: Mô phỏng Decision Engine sinh ra mã lý do tương lai (Future/Custom Reason Code)
        const string futureReasonCode = "ML_TRUST_SCORE_DECAY_EVALUATED";
        const string customReasonMessage = "Điểm tin cậy thích ứng giảm 15% do tần suất hủy giờ chót.";

        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.NEED_APPROVAL.ToString(),
            StrategyName = "AdaptiveTrustMLStrategy",
            StrategyVersion = "2.0.0",
            ExecutionTimeMs = 42,
            FallbackUsed = false,
            PrimaryReason = futureReasonCode,
            Reasons = [customReasonMessage],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        // Act: Serialize -> Wire JSON -> Deserialize
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DecisionResultDto>(json, JsonOptions);

        // Assert: Consumer nhận chuỗi lý do linh hoạt không bị crash
        deserialized.Should().NotBeNull();
        deserialized!.PrimaryReason.Should().Be(futureReasonCode);
        deserialized.Reasons.Should().ContainSingle().Which.Should().Be(customReasonMessage);
        deserialized.Status.Should().Be("NEED_APPROVAL");
    }

    [Fact]
    public void DecisionResultDto_WithFutureOrCustomReasonCode_ConformsToStringTypeInSchema()
    {
        // Arrange: Kiểm tra schema decision-result định nghĩa primaryReason là kiểu string mở (không bị lock bằng closed enum)
        var schemasDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/contracts/schemas"));
        var schemaPath = Path.Combine(schemasDir, "decision-result.schema.json");
        var schemaJson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(schemaPath))!.AsObject();

        // Assert: primaryReason là kiểu string, không có enum restriction
        var primaryReasonProp = schemaJson["properties"]!["primaryReason"]!.AsObject();
        primaryReasonProp["type"]!.ToString().Should().Be("string");
        primaryReasonProp.ContainsKey("enum").Should().BeFalse(
            "primaryReason must remain an open string type to allow future reason codes without breaking wire compatibility");
    }

    #endregion

    #region 5. Negative Cross-Field Invariant Fail-Fast Tests During ToDomain Mapping

    [Fact]
    public void DecisionMappingExtensions_ToDomain_WhenDepositExceedsTotalPrice_ThrowsDomainInvariantException()
    {
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            BookingType = "SINGLE",
            TotalPrice = 200000m,
            DepositAmount = 300000m, // Deposit > TotalPrice
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var act = () => dto.ToDomain();
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");
    }

    [Fact]
    public void DecisionMappingExtensions_ToDomain_WhenStartTimeAfterEndTime_ThrowsDomainInvariantException()
    {
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            EndTime = DateTime.UtcNow.AddDays(1), // EndTime < StartTime
            BookingType = "SINGLE",
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var act = () => dto.ToDomain();
        act.Should().Throw<DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_RANGE");
    }

    #endregion
}
