using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.MockAlternative;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03 / UT-05 (giết mutant của MT-02): thông báo <c>Reasons</c> theo từng thứ trong tuần, số tiền giá trị lớn, kiểm tra đối số null, chính sách cache theo TTL
/// và biên + thông báo của <c>MockAlternativeStrategyOptions.Validate</c>.
/// </summary>
public class MockAlternativeDecisionStrategyReasonsTests
{
    private static readonly MockAlternativeDecisionStrategy Strategy = new(new MockAlternativeStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("2026-11-16T10:00:00+07:00", "Khung giờ thấp điểm (10:00 thứ Hai, giờ chi nhánh) áp dụng cọc 30%.")]
    [InlineData("2026-11-17T10:00:00+07:00", "Khung giờ thấp điểm (10:00 thứ Ba, giờ chi nhánh) áp dụng cọc 30%.")]
    [InlineData("2026-11-18T10:00:00+07:00", "Khung giờ thấp điểm (10:00 thứ Tư, giờ chi nhánh) áp dụng cọc 30%.")]
    [InlineData("2026-11-19T10:00:00+07:00", "Khung giờ thấp điểm (10:00 thứ Năm, giờ chi nhánh) áp dụng cọc 30%.")]
    [InlineData("2026-11-20T10:00:00+07:00", "Khung giờ thấp điểm (10:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 30%.")]
    [InlineData("2026-11-21T10:00:00+07:00", "Khung giờ cao điểm (10:00 thứ Bảy, giờ chi nhánh) áp dụng cọc 50%.")]
    [InlineData("2026-11-22T10:00:00+07:00", "Khung giờ cao điểm (10:00 Chủ Nhật, giờ chi nhánh) áp dụng cọc 50%.")]
    public async Task EvaluateAsync_ReasonNamesLocalDayAndRate(string startIso, string expectedReason)
    {
        var start = DateTimeOffset.Parse(startIso, System.Globalization.CultureInfo.InvariantCulture);
        var reservation = DomainTestData.CreateReservation(items: [DomainTestData.CreateItem(start)]);

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be(expectedReason);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public async Task EvaluateAsync_HighValueAtPeak_ReasonShowsFormattedAmountCurrencyAndSlot()
    {
        var reservation = DomainTestData.CreateReservation(totalPrice: 2_500_000m);

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be("Đơn giá trị lớn (2,500,000 VND) vào giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh).");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var create = () => new MockAlternativeDecisionStrategy(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NullArguments_ThrowArgumentNullException()
    {
        var withoutReservation = async () => await Strategy.EvaluateAsync(null!, DomainTestData.CreateContext(), CancellationToken.None);
        var withoutContext = async () => await Strategy.EvaluateAsync(DomainTestData.CreateReservation(), null!, CancellationToken.None);

        (await withoutReservation.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("reservation");
        (await withoutContext.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("context");
    }

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(0)]
    [InlineData(30)]
    public void CachePolicy_FollowsTtl(int minutes)
    {
        var strategy = new MockAlternativeDecisionStrategy(new MockAlternativeStrategyOptions { CacheTtlMinutes = minutes });

        if (minutes > 0)
        {
            strategy.CachePolicy.Should().BeEquivalentTo(Shared.DecisionContractSDK.Decisions.StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(minutes)));
        }
        else
        {
            strategy.CachePolicy.Should().BeEquivalentTo(Shared.DecisionContractSDK.Decisions.StrategyCachePolicy.Disabled);
        }
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Validate_HighValueThresholdBoundary(int threshold, bool valid)
    {
        var validate = () => new MockAlternativeStrategyOptions { HighValueThreshold = threshold }.Validate();

        if (valid)
        {
            validate.Should().NotThrow("0 là biên hợp lệ");
        }
        else
        {
            validate.Should().Throw<ArgumentException>().WithMessage("HighValueThreshold phải ≥ 0.*");
        }
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("PeakDepositRate", "0", true)]
    [InlineData("PeakDepositRate", "1", true)]
    [InlineData("PeakDepositRate", "-0.0001", false)]
    [InlineData("PeakDepositRate", "1.0001", false)]
    [InlineData("OffPeakDepositRate", "0", true)]
    [InlineData("OffPeakDepositRate", "1", true)]
    [InlineData("OffPeakDepositRate", "-0.0001", false)]
    [InlineData("OffPeakDepositRate", "1.0001", false)]
    public void Validate_DepositRateBoundaries(string field, string rate, bool valid)
    {
        var value = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture);
        var options = field == "PeakDepositRate" ? new MockAlternativeStrategyOptions { PeakDepositRate = value } : new MockAlternativeStrategyOptions { OffPeakDepositRate = value };

        var validate = () => options.Validate();

        if (valid)
        {
            validate.Should().NotThrow();
        }
        else
        {
            validate.Should().Throw<ArgumentException>().WithMessage($"{field} phải trong [0, 1].*");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_TooManyDecimals_MessageNamesFieldAndLimit()
    {
        var validate = () => new MockAlternativeStrategyOptions { PeakDepositRate = 0.12345m }.Validate();

        validate.Should().Throw<ArgumentException>().WithMessage("PeakDepositRate không được có quá 4 chữ số thập phân.*");
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Validate_CacheTtlBoundary(int minutes, bool valid)
    {
        var validate = () => new MockAlternativeStrategyOptions { CacheTtlMinutes = minutes }.Validate();

        if (valid)
        {
            validate.Should().NotThrow("0 nghĩa là không cache");
        }
        else
        {
            validate.Should().Throw<ArgumentException>().WithMessage("CacheTtlMinutes phải ≥ 0.*");
        }
    }
}
