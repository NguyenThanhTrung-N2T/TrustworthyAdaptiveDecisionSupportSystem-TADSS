using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Governance;
using Xunit;

namespace TADSS.UnitTests.Application.Governance;

/// <summary>
/// UT-15: <see cref="OverrideRequestValidator"/> kiểm tra hình thức request override (F-04, F-17): đúng một trong rate/amount khi APPROVE,
/// không có cọc khi REJECT, rate trong [0, 1] với tối đa 4 chữ số thập phân, amount không âm/không vượt tổng tiền/không quá số chữ số
/// thập phân của tiền tệ, reason 5..500 code point (đếm theo Unicode, không theo UTF-16). Lỗi là <see cref="ContractValidationException"/>
/// <c>VALIDATION_ERROR</c> kèm đường dẫn JSON của trường sai.
/// </summary>
public class OverrideRequestValidatorTests
{
    private const decimal TotalPrice = 228_000m;
    private const string ValidReason = "Khách quen, chi nhánh bảo lãnh";

    private static OverrideRequestDto Approve(decimal? rate = null, decimal? amount = null, string reason = ValidReason) => new()
    {
        Action = OverrideAction.APPROVE,
        Deposit = new OverrideDepositDto { Rate = rate, Amount = amount },
        Reason = reason,
    };

    private static OverrideRequestDto Reject(OverrideDepositDto? deposit = null, string reason = ValidReason) => new()
    {
        Action = OverrideAction.REJECT,
        Deposit = deposit,
        Reason = reason,
    };

    private static void ValidateAll(OverrideRequestDto request, string currency = "VND")
    {
        OverrideRequestValidator.Validate(request);
        OverrideRequestValidator.ValidateAgainstOrder(request, TotalPrice, currency);
    }

    public static TheoryData<OverrideRequestDto, string> InvalidRequests => new()
    {
        { new OverrideRequestDto { Action = OverrideAction.APPROVE, Deposit = null, Reason = ValidReason }, "deposit" },
        { Approve(), "deposit" },
        { Approve(rate: 0.3m, amount: 68_400m), "deposit" },
        { Reject(new OverrideDepositDto { Rate = 0.3m }), "deposit" },
        { Reject(new OverrideDepositDto()), "deposit" },
        { Approve(rate: 1.00001m), "deposit.rate" },
        { Approve(rate: -0.1m), "deposit.rate" },
        { Approve(rate: 0.12345m), "deposit.rate" },
        { Approve(amount: -1m), "deposit.amount" },
        { Approve(amount: 228_001m), "deposit.amount" },
        { Approve(amount: 50_000.5m), "deposit.amount" },
        { Approve(rate: 0.3m, reason: "abcd"), "reason" },
        { Approve(rate: 0.3m, reason: new string('a', 501)), "reason" },
        { Approve(rate: 0.3m, reason: "      "), "reason" },
        { Reject(reason: "😀😀😀😀"), "reason" },
        { Reject(reason: string.Concat(Enumerable.Repeat("😀", 501))), "reason" },
    };

    /// <summary>Mỗi request sai ném <c>VALIDATION_ERROR</c> đúng <c>field</c> (thứ tự kiểm tra hình thức rồi đối chiếu đơn).</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(InvalidRequests))]
    public void Validate_InvalidRequest_Throws400_Theory(OverrideRequestDto request, string expectedField)
    {
        var act = () => ValidateAll(request);

        var exception = act.Should().Throw<ContractValidationException>().Which;
        exception.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        exception.FieldName.Should().Be(expectedField);
        exception.Message.Should().NotBeNullOrWhiteSpace();
    }

    public static TheoryData<OverrideRequestDto, string> ValidRequests => new()
    {
        { Approve(rate: 0.3m), "VND" },
        { Approve(rate: 0m), "VND" },
        { Approve(rate: 1m), "VND" },
        { Approve(rate: 0.5000m), "VND" },
        { Approve(amount: 0m), "VND" },
        { Approve(amount: TotalPrice), "VND" },
        { Approve(amount: 100.25m), "USD" },
        { Reject(), "VND" },
        { Approve(rate: 0.3m, reason: "😀😀😀😀😀"), "VND" },
        { Approve(rate: 0.3m, reason: string.Concat(Enumerable.Repeat("😀", 500))), "VND" },
        { Approve(rate: 0.3m, reason: new string('a', 5)), "VND" },
    };

    /// <summary>Request đúng hình thức không ném lỗi; reason tính theo code point (5 emoji = 10 đơn vị UTF-16 vẫn hợp lệ).</summary>
    [Theory]
    [Trait("CatalogId", "UT-15")]
    [MemberData(nameof(ValidRequests))]
    public void Validate_ValidRequest_DoesNotThrow_Theory(OverrideRequestDto request, string currency)
    {
        var act = () => ValidateAll(request, currency);

        act.Should().NotThrow();
    }

    /// <summary>Số tiền cọc có phần lẻ hợp lệ với tiền tệ 2 chữ số (USD) nhưng không với VND.</summary>
    [Fact]
    [Trait("CatalogId", "UT-15")]
    public void ValidateAgainstOrder_AmountDecimalsDependOnCurrency()
    {
        var request = Approve(amount: 10.25m);

        var usd = () => OverrideRequestValidator.ValidateAgainstOrder(request, TotalPrice, "USD");
        var vnd = () => OverrideRequestValidator.ValidateAgainstOrder(request, TotalPrice, "VND");

        usd.Should().NotThrow();
        vnd.Should().Throw<ContractValidationException>().Which.FieldName.Should().Be("deposit.amount");
    }
}
