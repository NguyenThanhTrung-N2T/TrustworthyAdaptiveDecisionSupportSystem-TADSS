using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Xunit;

namespace TADSS.UnitTests.Contracts;

/// <summary>
/// Bộ kiểm thử xác thực dữ liệu đầu vào DTOs (Data Annotation & Contract Validation Tests).
/// Đảm bảo tất cả các ràng buộc [Required], [Range] và quy tắc định dạng được áp dụng chính xác.
/// </summary>
public class DtoValidationContractTests
{
    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, serviceProvider: null, items: null);
        Validator.TryValidateObject(model, validationContext, validationResults, validateAllProperties: true);
        return validationResults;
    }

    #region 1. DecisionRequestDto Validation

    [Fact]
    public void DecisionRequestDto_ValidPayload_PassesValidation()
    {
        // Arrange
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            BookingType = "SINGLE",
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string> { ["source"] = "MobileApp" }
        };

        // Act
        var results = ValidateModel(dto);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void DecisionRequestDto_NegativeTotalPrice_FailsRangeValidation()
    {
        // Arrange
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            TotalPrice = -1000m, // Negative
            DepositAmount = 0m,
            PaymentStatus = "UNPAID",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var results = ValidateModel(dto);

        // Assert
        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(DecisionRequestDto.TotalPrice)));
    }

    [Fact]
    public void DecisionRequestDto_NegativeDepositAmount_FailsRangeValidation()
    {
        // Arrange
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            TotalPrice = 200000m,
            DepositAmount = -50000m, // Negative
            PaymentStatus = "UNPAID",
            RequestedAt = DateTime.UtcNow
        };

        // Act
        var results = ValidateModel(dto);

        // Assert
        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(DecisionRequestDto.DepositAmount)));
    }

    [Fact]
    public void DecisionRequestDto_CrossFieldInvariants_PassSyntaxValidation_AndFailDomainMapping()
    {
        // 1. Deposit exceeds TotalPrice
        var invalidDepositDto = new DecisionRequestDto
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

        // Syntax level validation (DataAnnotations) passes because both fields are non-negative decimals
        var validationResults = ValidateModel(invalidDepositDto);
        validationResults.Should().BeEmpty(
            "single-property DataAnnotations validate format/syntax; cross-field business invariants belong to Domain Layer");

        // Domain mapping level fails fast
        var actDomain = () => Shared.DecisionContractSDK.DTOs.Mappings.DecisionMappingExtensions.ToDomain(invalidDepositDto);
        actDomain.Should().Throw<Shared.DecisionContractSDK.Domain.DomainInvariantException>()
            .Where(e => e.InvariantCode == "DEPOSIT_EXCEEDS_TOTAL_PRICE");

        // 2. StartTime after EndTime
        var invalidScheduleDto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "GOLD",
            StartTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            EndTime = DateTime.UtcNow.AddDays(1), // Inverted
            BookingType = "SINGLE",
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        // Syntax level validation passes
        ValidateModel(invalidScheduleDto).Should().BeEmpty();

        // Domain mapping fails fast
        var actSchedule = () => Shared.DecisionContractSDK.DTOs.Mappings.DecisionMappingExtensions.ToDomain(invalidScheduleDto);
        actSchedule.Should().Throw<Shared.DecisionContractSDK.Domain.DomainInvariantException>()
            .Where(e => e.InvariantCode == "INVALID_SCHEDULE_RANGE");
    }

    #endregion

    #region 2. OverrideDecisionRequestDto Validation

    [Fact]
    public void OverrideDecisionRequestDto_ValidPayload_PassesValidation()
    {
        // Arrange
        var dto = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverrideReason = "Khách hàng VIP ngoại lệ được duyệt theo yêu cầu của Quản lý chi nhánh.",
            OverriddenBy = "Manager_NguyenVanA"
        };

        // Act
        var results = ValidateModel(dto);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void OverrideDecisionRequestDto_EmptyRequiredFields_FailsValidation()
    {
        // Arrange: Missing OverriddenBy, Decision status empty, OverrideReason short
        var dto = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = string.Empty },
            OverrideReason = "abc", // Less than 5 chars
            OverriddenBy = string.Empty // Empty
        };

        // Act
        var results = ValidateModel(dto);

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverrideReason)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverriddenBy)));
    }

    #endregion

    #region 3. Common ApiResponse & PagedResult Validation

    [Fact]
    public void ApiResponse_Generic_OkAndFailFactories_ProduceValidStructures()
    {
        // Act - Ok
        var okResponse = Shared.DecisionContractSDK.Common.ApiResponse<string>.Ok("CustomData", "Thành công");
        okResponse.Success.Should().BeTrue();
        okResponse.Data.Should().Be("CustomData");
        okResponse.Message.Should().Be("Thành công");
        okResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Success);
        okResponse.Errors.Should().BeNull();

        // Act - Fail
        var errors = new Dictionary<string, string[]> { ["field1"] = ["Lỗi 1", "Lỗi 2"] };
        var failResponse = Shared.DecisionContractSDK.Common.ApiResponse<string>.Fail("Thất bại", Shared.DecisionContractSDK.Common.ErrorCodes.ValidationError, errors);
        failResponse.Success.Should().BeFalse();
        failResponse.Data.Should().BeNull();
        failResponse.Message.Should().Be("Thất bại");
        failResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.ValidationError);
        failResponse.Errors.Should().NotBeNull();
        failResponse.Errors!["field1"].Should().HaveCount(2);
    }

    [Fact]
    public void ApiResponse_NonGeneric_OkAndFailFactories_ProduceExpectedStructures()
    {
        // Act - Ok
        var okResponse = Shared.DecisionContractSDK.Common.ApiResponse.Ok(new { Id = 123 }, "Success Non-Generic");
        okResponse.Success.Should().BeTrue();
        okResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Success);
        okResponse.Message.Should().Be("Success Non-Generic");
        okResponse.Data.Should().NotBeNull();

        // Act - Fail
        var errors = new Dictionary<string, string[]> { ["general"] = ["Something went wrong"] };
        var failResponse = Shared.DecisionContractSDK.Common.ApiResponse.Fail("Failed Non-Generic", Shared.DecisionContractSDK.Common.ErrorCodes.InternalError, errors);
        failResponse.Success.Should().BeFalse();
        failResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.InternalError);
        failResponse.Message.Should().Be("Failed Non-Generic");
        failResponse.Errors.Should().NotBeNull();
        failResponse.Errors!["general"].Should().ContainSingle("Something went wrong");
    }

    [Fact]
    public void ContractValidationException_Instantiates_WithAllPropertiesAssigned()
    {
        var ex = new Shared.DecisionContractSDK.Common.ContractValidationException("INVALID_PAYLOAD", "Payload is malformed", "RequestBody");
        ex.ErrorCode.Should().Be("INVALID_PAYLOAD");
        ex.Message.Should().Be("Payload is malformed");
        ex.FieldName.Should().Be("RequestBody");
    }

    [Theory]
    [InlineData(0, 10, 1, 0, false, false)]
    [InlineData(25, 10, 1, 3, true, false)]
    [InlineData(25, 10, 2, 3, true, true)]
    [InlineData(25, 10, 3, 3, false, true)]
    public void PagedResult_EdgeCases_CalculatesPagingAccurately(
        int totalItems, int pageSize, int page, int expectedTotalPages, bool expectedHasNext, bool expectedHasPrev)
    {
        var paged = new Shared.DecisionContractSDK.Common.PagedResult<string>
        {
            Items = new List<string>(),
            TotalItems = totalItems,
            PageSize = pageSize,
            Page = page
        };

        paged.TotalPages.Should().Be(expectedTotalPages);
        paged.HasNext.Should().Be(expectedHasNext);
        paged.HasPrev.Should().Be(expectedHasPrev);
    }

    [Fact]
    public void ApiResponse_NullDataWithSuccessTrue_IsValidContract()
    {
        // Act - Generic
        var genericResponse = Shared.DecisionContractSDK.Common.ApiResponse<string?>.Ok(null, "Resource processed with empty response body");
        genericResponse.Success.Should().BeTrue();
        genericResponse.Data.Should().BeNull();
        genericResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Success);
        genericResponse.Message.Should().Be("Resource processed with empty response body");

        // Act - Non Generic
        var nonGenericResponse = Shared.DecisionContractSDK.Common.ApiResponse.Ok(null, "No data payload");
        nonGenericResponse.Success.Should().BeTrue();
        nonGenericResponse.Data.Should().BeNull();
        nonGenericResponse.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Success);
    }

    [Fact]
    public void DecisionRequestDto_BoundaryAndNullFields_ValidationScenarios()
    {
        // Zero price / zero deposit (valid in DTO layer for promotional / sponsored courts)
        var dto = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            CourtTypeId = null, // Optional
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "STANDARD",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            BookingType = "SINGLE",
            TotalPrice = 0m,
            DepositAmount = 0m,
            PaymentStatus = "PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow,
            Metadata = null // Optional metadata
        };

        var results = ValidateModel(dto);
        results.Should().BeEmpty();
    }

    [Fact]
    public void OverrideDecisionRequestDto_MaxLength_FailsValidation()
    {
        var longReason = new string('R', 501); // Exceeds 500 max length
        var longUser = new string('U', 101); // Exceeds 100 max length

        var dto = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverrideReason = longReason,
            OverriddenBy = longUser
        };

        var results = ValidateModel(dto);
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverrideReason)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(OverrideDecisionRequestDto.OverriddenBy)));
    }

    [Fact]
    public void OverrideTargetPayloadDto_Validation_WorksAsExpected()
    {
        var valid = new OverrideTargetPayloadDto { Status = "APPROVED" };
        valid.Status.Should().Be("APPROVED");
        valid.Status = "REJECTED";
        valid.Status.Should().Be("REJECTED");
        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();

        var invalid = new OverrideTargetPayloadDto { Status = string.Empty };
        var invalidResults = ValidateModel(invalid);
        invalidResults.Should().ContainSingle(r => r.MemberNames.Contains(nameof(OverrideTargetPayloadDto.Status)));
    }

    #endregion
}

