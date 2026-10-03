using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Common;
using TADSS.Application.Decisions;

namespace TADSS.Application.Governance;

/// <summary>
/// Kiểm tra request override (INTEGRATION_SPEC §3.6; F-04, F-17). Thuần, không I/O. Lỗi ném <see cref="ContractValidationException"/>
/// với mã <c>VALIDATION_ERROR</c> và <c>FieldName</c> là đường dẫn JSON của trường sai (<c>deposit</c>, <c>deposit.rate</c>, <c>deposit.amount</c>, <c>reason</c>).
/// <para>
/// Chia hai bước vì <c>totalPrice</c>/<c>currency</c> nằm trong quyết định đã lưu: <see cref="Validate"/> chỉ cần request (chạy trước mọi truy cập kho),
/// <see cref="ValidateAgainstOrder"/> đối chiếu số tiền cọc với tổng tiền và số chữ số thập phân của tiền tệ.
/// </para>
/// </summary>
public static class OverrideRequestValidator
{
    private const string DepositField = "deposit";
    private const string RateField = "deposit.rate";
    private const string AmountField = "deposit.amount";
    private const string ReasonField = "reason";

    /// <summary>
    /// Kiểm tra hình thức: <c>APPROVE</c> có đúng một trong <c>deposit.rate</c>/<c>deposit.amount</c>, <c>REJECT</c> không có <c>deposit</c>;
    /// <c>rate</c> trong <c>[0, 1]</c> với tối đa 4 chữ số thập phân; <c>amount</c> không âm; <c>reason</c> 5..500 code point và không chỉ gồm khoảng trắng.
    /// </summary>
    /// <param name="request">Request override.</param>
    /// <exception cref="ContractValidationException"><c>VALIDATION_ERROR</c> kèm đường dẫn trường sai.</exception>
    public static void Validate(OverrideRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateReason(request.Reason);

        switch (request.Action)
        {
            case OverrideAction.APPROVE:
                ValidateApproveDeposit(request.Deposit);
                break;
            case OverrideAction.REJECT:
                if (request.Deposit is not null)
                {
                    throw Invalid(DepositField, "REJECT không được kèm deposit.");
                }

                break;
            default:
                throw Invalid("action", "action không hợp lệ.");
        }
    }

    /// <summary>
    /// Đối chiếu số tiền cọc (nếu có) với đơn: <c>amount</c> không vượt <paramref name="totalPrice"/> và không nhiều chữ số thập phân hơn tiền tệ.
    /// Giả định <see cref="Validate"/> đã chạy.
    /// </summary>
    /// <param name="request">Request override đã qua <see cref="Validate"/>.</param>
    /// <param name="totalPrice">Tổng tiền của quyết định (từ <c>request_payload</c>).</param>
    /// <param name="currency">Tiền tệ của quyết định (từ <c>request_payload</c>).</param>
    /// <exception cref="ContractValidationException"><c>VALIDATION_ERROR</c> với <c>FieldName</c> là <c>deposit.amount</c>.</exception>
    public static void ValidateAgainstOrder(OverrideRequestDto request, decimal totalPrice, string currency)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Deposit?.Amount is not { } amount)
        {
            return;
        }

        if (amount > totalPrice)
        {
            throw Invalid(AmountField, "deposit.amount không được lớn hơn tổng tiền của quyết định.");
        }

        var minorUnits = DepositCalculator.GetMinorUnits(currency);
        if (decimal.Round(amount, minorUnits, MidpointRounding.AwayFromZero) != amount)
        {
            throw Invalid(AmountField, $"deposit.amount không được nhiều hơn {minorUnits} chữ số thập phân với tiền tệ {currency}.");
        }
    }

    private static void ValidateApproveDeposit(OverrideDepositDto? deposit)
    {
        if (deposit is null || (deposit.Rate is null && deposit.Amount is null))
        {
            throw Invalid(DepositField, "APPROVE phải có deposit.rate hoặc deposit.amount.");
        }

        if (deposit.Rate is not null && deposit.Amount is not null)
        {
            throw Invalid(DepositField, "deposit chỉ được có một trong rate hoặc amount, không được gửi cả hai.");
        }

        if (deposit.Rate is { } rate)
        {
            if (rate is < 0m or > 1m)
            {
                throw Invalid(RateField, "deposit.rate phải nằm trong [0, 1].");
            }

            if (decimal.Round(rate, DecisionResponseComposer.MaxRateDecimals) != rate)
            {
                throw Invalid(RateField, $"deposit.rate không được nhiều hơn {DecisionResponseComposer.MaxRateDecimals} chữ số thập phân.");
            }
        }

        if (deposit.Amount is < 0m)
        {
            throw Invalid(AmountField, "deposit.amount không được âm.");
        }
    }

    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw Invalid(ReasonField, "reason không được để trống.");
        }

        // Đếm theo Unicode code point như char_length của PostgreSQL (UTF-16 đếm emoji thành 2 đơn vị)
        var length = reason.EnumerateRunes().Count();
        if (length < ContractLimits.MinReasonLength || length > ContractLimits.MaxReasonLength)
        {
            throw Invalid(ReasonField, $"reason phải dài {ContractLimits.MinReasonLength}..{ContractLimits.MaxReasonLength} ký tự.");
        }
    }

    private static ContractValidationException Invalid(string field, string message) =>
        new(ErrorCodes.ValidationError, message, field);
}
