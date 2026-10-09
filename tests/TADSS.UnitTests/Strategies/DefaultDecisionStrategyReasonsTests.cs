using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03 / UT-05 (giết mutant của MT-02): nội dung <c>Reasons</c> mà Manager đọc (số tiền, ngưỡng, hạng, phần trăm), kiểm tra đối số null
/// và các biên + thông báo của <c>DefaultStrategyOptions.Validate</c>. Mỗi khẳng định gắn với một giá trị cụ thể để đổi chuỗi/phép tính là test đỏ.
/// </summary>
public class DefaultDecisionStrategyReasonsTests
{
    private static readonly DefaultDecisionStrategy Strategy = new(new DefaultStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(CustomerTier.STANDARD, "Áp dụng cọc 50% cho hạng STANDARD.")]
    [InlineData(CustomerTier.SILVER, "Áp dụng cọc 40% cho hạng SILVER.")]
    [InlineData(CustomerTier.GOLD, "Áp dụng cọc 30% cho hạng GOLD.")]
    [InlineData(CustomerTier.PLATINUM, "Áp dụng cọc 20% cho hạng PLATINUM.")]
    [InlineData(CustomerTier.DIAMOND, "Áp dụng cọc 20% cho hạng DIAMOND.")]
    public async Task EvaluateAsync_Member_ReasonNamesTierAndPercent(CustomerTier tier, string expectedReason)
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(tier));

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be(expectedReason);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_Guest_ReasonNamesGuestSegment()
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateGuest());

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be("Áp dụng cọc 50% cho khách vãng lai.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_FractionalRate_IsFormattedWithUpToTwoDecimals()
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { GoldDepositRate = 0.3333m });
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(CustomerTier.GOLD));

        var decision = await strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be("Áp dụng cọc 33.33% cho hạng GOLD.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_HighValue_ReasonShowsAmountThresholdAndCurrency()
    {
        var reservation = DomainTestData.CreateReservation(totalPrice: 2_500_000m);

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be("Giá trị đơn 2,500,000 VND đạt ngưỡng cần duyệt 2,000,000 VND.");
        decision.Recommendations.Should().ContainSingle().Which.Description.Should().Be("Manager xem xét đơn giá trị lớn và quyết định mức cọc.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NoShowHistory_ReasonShowsCountAndThreshold()
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(CustomerTier.GOLD, DomainTestData.CreateHistory(noShowCount: 3)));

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle().Which.Should().Be("Khách có 3 lần không đến (ngưỡng 2).");
        decision.Recommendations.Should().ContainSingle().Which.Description.Should().Be("Manager xem xét lịch sử không đến của khách và quyết định mức cọc.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var create = () => new DefaultDecisionStrategy(null!);

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
    [Trait("CatalogId", "UT-05")]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Validate_HighValueThresholdBoundary(int threshold, bool valid)
    {
        var validate = () => new DefaultStrategyOptions { HighValueThreshold = threshold }.Validate();

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
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Validate_NoShowReviewThresholdBoundary(int threshold, bool valid)
    {
        var validate = () => new DefaultStrategyOptions { NoShowReviewThreshold = threshold }.Validate();

        if (valid)
        {
            validate.Should().NotThrow("1 là biên hợp lệ");
        }
        else
        {
            validate.Should().Throw<ArgumentException>().WithMessage("NoShowReviewThreshold phải ≥ 1.*");
        }
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("0", true)]
    [InlineData("1", true)]
    [InlineData("-0.0001", false)]
    [InlineData("1.0001", false)]
    public void Validate_DepositRateBoundaries(string rate, bool valid)
    {
        var options = new DefaultStrategyOptions { StandardDepositRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) };

        var validate = () => options.Validate();

        if (valid)
        {
            validate.Should().NotThrow();
        }
        else
        {
            validate.Should().Throw<ArgumentException>().WithMessage("StandardDepositRate phải trong [0, 1].*");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void Validate_TooManyDecimals_MessageNamesFieldAndLimit()
    {
        var validate = () => new DefaultStrategyOptions { SilverDepositRate = 0.12345m }.Validate();

        validate.Should().Throw<ArgumentException>().WithMessage($"SilverDepositRate không được có quá 4 chữ số thập phân.*");
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void Validate_CacheTtlBoundary(int minutes, bool valid)
    {
        var validate = () => new DefaultStrategyOptions { CacheTtlMinutes = minutes }.Validate();

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
