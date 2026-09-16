using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Xunit;

namespace TADSS.UnitTests;

/// <summary>
/// Bộ kiểm thử xác thực dữ liệu đầu vào DTOs (Data Annotation & Contract Validation Tests).
/// Đảm bảo tất cả các ràng buộc [Required], [Range] và quy tắc định dạng được áp dụng chính xác.
/// </summary>
public class DTOValidationTests
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
    public void ApiResponse_OkFactory_ProducesValidSuccessStructure()
    {
        // Arrange & Act
        var response = Shared.DecisionContractSDK.Common.ApiResponse<string>.Ok("CustomData", "Thành công");

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().Be("CustomData");
        response.Message.Should().Be("Thành công");
        response.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.Success);
        response.Errors.Should().BeNull();
    }

    [Fact]
    public void ApiResponse_FailFactory_ProducesErrorStructure()
    {
        // Arrange & Act
        var errors = new Dictionary<string, string[]>
        {
            ["field1"] = ["Lỗi 1", "Lỗi 2"]
        };
        var response = Shared.DecisionContractSDK.Common.ApiResponse<string>.Fail("Thất bại", Shared.DecisionContractSDK.Common.ErrorCodes.ValidationError, errors);

        // Assert
        response.Success.Should().BeFalse();
        response.Data.Should().BeNull();
        response.Message.Should().Be("Thất bại");
        response.Code.Should().Be(Shared.DecisionContractSDK.Common.ErrorCodes.ValidationError);
        response.Errors.Should().NotBeNull();
        response.Errors!["field1"].Should().HaveCount(2);
    }

    [Fact]
    public void PagedResult_CalculatesPagingPropertiesAccurately()
    {
        // Arrange & Act
        var paged = new Shared.DecisionContractSDK.Common.PagedResult<int>
        {
            Items = [1, 2, 3, 4, 5],
            TotalItems = 25,
            Page = 2,
            PageSize = 5
        };

        // Assert
        paged.TotalPages.Should().Be(5);
        paged.HasNext.Should().BeTrue();
        paged.HasPrev.Should().BeTrue();
    }

    #endregion
}
