using FluentAssertions;
using TADSS.Application.Decisions;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-12: quy đổi tiền cọc (<see cref="DepositCalculator"/>) — <c>amount = round(totalPrice × rate)</c> theo số chữ số
/// thập phân của tiền tệ, làm tròn <see cref="MidpointRounding.AwayFromZero"/>, <c>rate</c> giữ nguyên và đầu vào
/// sai bị chặn bằng lỗi đối số.
/// </summary>
public class DepositCalculatorTests
{
    private readonly DepositCalculator _calculator = new();

    [Fact]
    [Trait("CatalogId", "UT-12")]
    public void Calculate_SpecSample_Returns114000()
    {
        var deposit = _calculator.Calculate(228_000m, 0.5m, "VND");

        deposit.Rate.Should().Be(0.5m);
        deposit.Amount.Should().Be(114_000m);
        deposit.Currency.Should().Be("VND");
    }

    public static TheoryData<decimal, decimal, decimal> VndRoundingCases => new()
    {
        { 100_001m, 0.333m, 33_300m }, // 33300.333 → làm tròn xuống
        { 1_001m, 0.5m, 501m },        // 500.5 → AwayFromZero (banker's rounding sẽ ra 500)
        { 1_000m, 0.0005m, 1m },       // 0.5 → 1
        { 999m, 0.0004m, 0m },         // 0.3996 → 0
    };

    [Theory]
    [Trait("CatalogId", "UT-12")]
    [MemberData(nameof(VndRoundingCases))]
    public void Calculate_VndRounding_Theory(decimal totalPrice, decimal rate, decimal expectedAmount)
    {
        var deposit = _calculator.Calculate(totalPrice, rate, "VND");

        deposit.Amount.Should().Be(expectedAmount);
        deposit.Rate.Should().Be(rate, "rate được giữ nguyên, không làm tròn");
    }

    public static TheoryData<string, decimal, decimal, decimal> TwoDecimalCases => new()
    {
        { "USD", 20.01m, 0.5m, 10.01m },  // 10.005 → 10.01 (không phải 10.00)
        { "EUR", 20.01m, 0.5m, 10.01m },
        { "GBP", 20.01m, 0.5m, 10.01m },  // mã lạ dùng 2 chữ số
        { "USD", 100m, 0.3333m, 33.33m }, // 33.33 → giữ nguyên
        { "EUR", 10.005m, 0.5m, 5.00m },  // 5.0025 → 5.00
    };

    [Theory]
    [Trait("CatalogId", "UT-12")]
    [MemberData(nameof(TwoDecimalCases))]
    public void Calculate_TwoDecimalCurrencies_Theory(string currency, decimal totalPrice, decimal rate, decimal expectedAmount)
    {
        var deposit = _calculator.Calculate(totalPrice, rate, currency);

        deposit.Amount.Should().Be(expectedAmount);
        deposit.Currency.Should().Be(currency);
    }

    [Theory]
    [Trait("CatalogId", "UT-12")]
    [InlineData("VND", 0)]
    [InlineData("USD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("GBP", 2)]
    public void GetMinorUnits_KnownAndUnknownCurrencies_Theory(string currency, int expectedDigits)
    {
        DepositCalculator.GetMinorUnits(currency).Should().Be(expectedDigits);
    }

    public static TheoryData<string, decimal, decimal> RateBoundaryCases => new()
    {
        { "VND", 0m, 0m },
        { "VND", 1m, 228_000m },
        { "VND", 1m, 228_000.4m },
        { "USD", 0m, 0m },
        { "USD", 1m, 20.01m },
    };

    [Theory]
    [Trait("CatalogId", "UT-12")]
    [MemberData(nameof(RateBoundaryCases))]
    public void Calculate_RateBoundaries_Theory(string currency, decimal rate, decimal totalPrice)
    {
        var deposit = _calculator.Calculate(totalPrice, rate, currency);

        var expected = rate == 0m
            ? 0m
            : Math.Round(totalPrice, DepositCalculator.GetMinorUnits(currency), MidpointRounding.AwayFromZero);
        deposit.Amount.Should().Be(expected);
        deposit.Rate.Should().Be(rate);
    }

    [Fact]
    [Trait("CatalogId", "UT-12")]
    public void Calculate_ZeroTotalPrice_ReturnsZeroAmount()
    {
        var deposit = _calculator.Calculate(0m, 0.5m, "VND");

        deposit.Amount.Should().Be(0m);
    }

    [Theory]
    [Trait("CatalogId", "UT-12")]
    [InlineData(-0.01, 228_000, "VND")]
    [InlineData(1.01, 228_000, "VND")]
    [InlineData(0.5, -1, "VND")]
    [InlineData(0.5, 228_000, "vnd")]
    [InlineData(0.5, 228_000, "VN")]
    [InlineData(0.5, 228_000, "VNDD")]
    [InlineData(0.5, 228_000, "")]
    [InlineData(0.5, 228_000, "VN1")]
    [InlineData(0.5, 228_000, "VN\n")]
    public void Calculate_InvalidInputs_Throws_Theory(double rate, double totalPrice, string currency)
    {
        var act = () => _calculator.Calculate((decimal)totalPrice, (decimal)rate, currency);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-12")]
    public void GetMinorUnits_InvalidCurrency_Throws()
    {
        var act = () => DepositCalculator.GetMinorUnits("vnd");

        act.Should().Throw<ArgumentException>();
    }

    public static TheoryData<string, decimal, decimal, decimal> FromAmountCases => new()
    {
        { "VND", 228_000m, 50_000m, 0.2193m },  // 0.219298 → 0.2193
        { "VND", 228_000m, 228_000m, 1m },
        { "VND", 228_000m, 0m, 0m },
        { "VND", 0m, 0m, 0m },                  // tổng 0 thì rate 0
        { "VND", 20_000m, 1m, 0.0001m },        // 0.00005 → AwayFromZero (banker's rounding sẽ ra 0.0000)
        { "VND", 30_000m, 1m, 0.0000m },        // 0.0000333 → 0
        { "VND", 3m, 1m, 0.3333m },             // 0.33333 → 0.3333
        { "VND", 3m, 2m, 0.6667m },             // 0.66666 → 0.6667
        { "USD", 100m, 33.33m, 0.3333m },
    };

    /// <summary>
    /// <c>FromAmount</c>: giữ nguyên <c>amount</c>, suy ra <c>rate = round(amount / total, 4, AwayFromZero)</c>; tổng 0 + amount 0 thì rate 0.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-12")]
    [MemberData(nameof(FromAmountCases))]
    public void FromAmount_Theory(string currency, decimal totalPrice, decimal amount, decimal expectedRate)
    {
        var deposit = _calculator.FromAmount(totalPrice, amount, currency);

        deposit.Rate.Should().Be(expectedRate);
        deposit.Amount.Should().Be(amount, "amount được giữ nguyên, không làm tròn");
        deposit.Currency.Should().Be(currency);
    }

    /// <summary><c>FromAmount</c> chặn tham số sai: amount âm/vượt tổng/nhiều chữ số thập phân hơn tiền tệ, tổng âm → lỗi ngoài miền; tiền tệ sai → lỗi đối số.</summary>
    [Theory]
    [Trait("CatalogId", "UT-12")]
    [InlineData(-0.01, 228_000, "VND", typeof(ArgumentOutOfRangeException))]
    [InlineData(1, 0, "VND", typeof(ArgumentOutOfRangeException))]
    [InlineData(228_001, 228_000, "VND", typeof(ArgumentOutOfRangeException))]
    [InlineData(100.5, 228_000, "VND", typeof(ArgumentOutOfRangeException))]
    [InlineData(10.005, 228_000, "USD", typeof(ArgumentOutOfRangeException))]
    [InlineData(5, -1, "VND", typeof(ArgumentOutOfRangeException))]
    [InlineData(5, 228_000, "vnd", typeof(ArgumentException))]
    public void FromAmount_InvalidInputs_Throws_Theory(double amount, double totalPrice, string currency, Type expectedException)
    {
        var act = () => _calculator.FromAmount((decimal)totalPrice, (decimal)amount, currency);

        act.Should().Throw<ArgumentException>().Which.Should().BeOfType(expectedException);
    }
}
