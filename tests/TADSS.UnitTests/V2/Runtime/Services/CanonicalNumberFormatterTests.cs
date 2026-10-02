using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using TADSS.Runtime.Api.V2.Services;
using TADSS.Runtime.Api.V2.Services.Strategies;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Services;

/// <summary>
/// UT-13 / UT-06: <see cref="CanonicalNumberFormatter"/> là quy tắc duy nhất ghi số thập phân cho <c>request_hash</c> và
/// <c>optionsFingerprint</c>; test ghim các giá trị biên và chứng minh <see cref="OptionsFingerprintBuilder"/> dùng đúng
/// chuỗi đó (để hai nơi không thể lệch nhau).
/// </summary>
public class CanonicalNumberFormatterTests
{
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("0", "0")]
    [InlineData("0.00", "0")]
    [InlineData("-0.00", "0")]
    [InlineData("228000.00", "228000")]
    [InlineData("0.50", "0.5")]
    [InlineData("0.5", "0.5")]
    [InlineData("100", "100")]
    [InlineData("1E+2", "100")]
    [InlineData("1.2300", "1.23")]
    [InlineData("-3.10", "-3.1")]
    public void Format_EdgeValues_AreCanonical_Theory(string literal, string expected)
    {
        var value = decimal.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);

        CanonicalNumberFormatter.Format(value).Should().Be(expected);
    }

    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("0.00", "0")]
    [InlineData("-0.00", "0")]
    [InlineData("228000.00", "228000")]
    [InlineData("0.50", "0.5")]
    [InlineData("1E+2", "100")]
    public void FingerprintBuilder_Decimal_UsesSharedCanonicalText_Theory(string literal, string expectedText)
    {
        var value = decimal.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
        var builder = new OptionsFingerprintBuilder();
        builder.Add("X", value);

        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("X=" + expectedText)))
            .ToLowerInvariant()[..16];

        builder.Build().Should().Be(expected);
    }
}
