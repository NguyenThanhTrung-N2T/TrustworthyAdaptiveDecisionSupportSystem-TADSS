using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Application.Decisions;

/// <summary>
/// Quy đổi tỷ lệ cọc thành số tiền cọc (INTEGRATION_SPEC §3.3): <c>amount = round(totalPrice × rate)</c> theo số chữ
/// số thập phân của tiền tệ, làm tròn <see cref="MidpointRounding.AwayFromZero"/>. Đây là nơi duy nhất quy đổi tiền cọc
/// của runtime; đầu vào đã được domain/strategy bảo đảm nên vi phạm là lỗi lập trình (ném lỗi đối số).
/// </summary>
public sealed class DepositCalculator
{
    private const int DefaultMinorUnits = 2;

    /// <summary>
    /// Tính mức cọc cho một đơn.
    /// </summary>
    /// <param name="totalPrice">Tổng tiền đơn, không âm.</param>
    /// <param name="rate">Tỷ lệ cọc trong <c>[0, 1]</c>; được giữ nguyên trong kết quả, không làm tròn.</param>
    /// <param name="currency">Mã tiền tệ ISO 4217 gồm đúng ba chữ cái in hoa.</param>
    /// <returns>Mức cọc gồm tỷ lệ, số tiền đã làm tròn và tiền tệ.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> ngoài <c>[0, 1]</c> hoặc <paramref name="totalPrice"/> âm.</exception>
    /// <exception cref="ArgumentException"><paramref name="currency"/> không khớp <c>^[A-Z]{3}\z</c>.</exception>
    public DepositDto Calculate(decimal totalPrice, decimal rate, string currency)
    {
        EnsureValidCurrency(currency);

        if (rate is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "Tỷ lệ cọc phải nằm trong [0, 1].");
        }

        if (totalPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPrice), totalPrice, "Tổng tiền không được âm.");
        }

        var amount = Math.Round(totalPrice * rate, GetMinorUnits(currency), MidpointRounding.AwayFromZero);

        return new DepositDto { Rate = rate, Amount = amount, Currency = currency };
    }

    /// <summary>
    /// Suy ra tỷ lệ cọc từ số tiền cọc Manager chọn: <c>rate = round(amount / totalPrice, 4)</c> làm tròn
    /// <see cref="MidpointRounding.AwayFromZero"/> (cột <c>numeric(5,4)</c>); <paramref name="amount"/> được giữ nguyên.
    /// Tổng tiền bằng 0 thì <paramref name="amount"/> phải là 0 và tỷ lệ là 0.
    /// </summary>
    /// <param name="totalPrice">Tổng tiền đơn, không âm.</param>
    /// <param name="amount">Số tiền cọc trong <c>[0, totalPrice]</c>, không nhiều chữ số thập phân hơn tiền tệ cho phép.</param>
    /// <param name="currency">Mã tiền tệ ISO 4217 gồm đúng ba chữ cái in hoa.</param>
    /// <returns>Mức cọc gồm tỷ lệ đã làm tròn, số tiền nguyên bản và tiền tệ.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalPrice"/> âm; hoặc <paramref name="amount"/> âm, lớn hơn <paramref name="totalPrice"/> hay có nhiều chữ số thập phân hơn tiền tệ.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="currency"/> không khớp <c>^[A-Z]{3}\z</c>.</exception>
    public DepositDto FromAmount(decimal totalPrice, decimal amount, string currency)
    {
        var minorUnits = GetMinorUnits(currency);

        if (totalPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPrice), totalPrice, "Tổng tiền không được âm.");
        }

        if (amount < 0m || amount > totalPrice)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Số tiền cọc phải nằm trong [0, tổng tiền].");
        }

        if (decimal.Round(amount, minorUnits, MidpointRounding.AwayFromZero) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Số tiền cọc không được nhiều hơn {minorUnits} chữ số thập phân.");
        }

        var rate = totalPrice == 0m
            ? 0m
            : Math.Round(amount / totalPrice, DecisionResponseComposer.MaxRateDecimals, MidpointRounding.AwayFromZero);

        return new DepositDto { Rate = rate, Amount = amount, Currency = currency };
    }

    /// <summary>
    /// Số chữ số thập phân của tiền tệ: <c>VND</c> 0; <c>EUR</c>, <c>USD</c> và mọi tiền tệ khác 2.
    /// </summary>
    /// <param name="currency">Mã tiền tệ ISO 4217 gồm đúng ba chữ cái in hoa.</param>
    /// <exception cref="ArgumentException"><paramref name="currency"/> không khớp <c>^[A-Z]{3}\z</c>.</exception>
    public static int GetMinorUnits(string currency)
    {
        EnsureValidCurrency(currency);

        return currency == "VND" ? 0 : DefaultMinorUnits;
    }

    private static void EnsureValidCurrency(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (currency.Length != 3 || !currency.All(character => character is >= 'A' and <= 'Z'))
        {
            throw new ArgumentException("Mã tiền tệ phải gồm đúng ba chữ cái in hoa (ISO 4217).", nameof(currency));
        }
    }
}
