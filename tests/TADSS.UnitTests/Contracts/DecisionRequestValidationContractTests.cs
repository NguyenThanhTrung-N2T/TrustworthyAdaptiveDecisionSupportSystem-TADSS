using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Contracts;

public class DecisionRequestValidationContractTests
{
    [Fact]
    public void DataAnnotations_WithValidRequest_ReturnNoErrors()
    {
        var results = ValidationHelper.Validate(DecisionRequestFactory.ValidDecisionRequest());

        results.Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(DecisionRequestDto.BookingType))]
    [InlineData(nameof(DecisionRequestDto.PaymentStatus))]
    public void DataAnnotations_WithMissingRequiredString_ReportSpecificMember(string propertyName)
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        typeof(DecisionRequestDto).GetProperty(propertyName)!.SetValue(request, string.Empty);

        var results = ValidationHelper.Validate(request);

        results.Should().Contain(result => result.MemberNames.Contains(propertyName));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-100000)]
    public void DataAnnotations_WithNegativeTotalPrice_ReportRangeViolation(decimal totalPrice)
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        request.TotalPrice = totalPrice;

        var results = ValidationHelper.Validate(request);

        results.Should().ContainSingle(
            result => result.MemberNames.Contains(nameof(DecisionRequestDto.TotalPrice)));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1)]
    [InlineData(-100000)]
    public void DataAnnotations_WithNegativeDepositAmount_ReportRangeViolation(decimal depositAmount)
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        request.DepositAmount = depositAmount;

        var results = ValidationHelper.Validate(request);

        results.Should().ContainSingle(
            result => result.MemberNames.Contains(nameof(DecisionRequestDto.DepositAmount)));
    }

    [Fact]
    public void DataAnnotations_WithNullMetadata_IsAllowed()
    {
        var request = DecisionRequestFactory.ValidDecisionRequest();
        request.Metadata = null;

        var results = ValidationHelper.Validate(request);

        results.Should().BeEmpty();
    }
}
