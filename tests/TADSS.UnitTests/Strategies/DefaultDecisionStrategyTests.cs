using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03: bảng quyết định <c>DefaultDecisionStrategy</c> 2.0.0 — mọi ô và biên (giá trị lớn, lịch sử không đến,
/// từng hạng, khách vãng lai), tham số cấu hình được, không phụ thuộc thời điểm đánh giá.
/// </summary>
public class DefaultDecisionStrategyTests
{
    private static readonly DefaultDecisionStrategy Strategy = new(new DefaultStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(CustomerTier.STANDARD, false, 228_000, 0, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.SILVER, false, 228_000, 0, DecisionStatus.APPROVED, "0.4", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 0, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.PLATINUM, false, 228_000, 0, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.DIAMOND, false, 228_000, 0, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.STANDARD, true, 228_000, 0, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 1_999_999, 0, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 2_000_000, 0, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 1, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData(CustomerTier.GOLD, false, 228_000, 2, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.NoShowHistoryReview)]
    [InlineData(CustomerTier.DIAMOND, false, 2_000_000, 5, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    public async Task EvaluateAsync_DecisionTable_ReturnsExpectedDecision(
        CustomerTier tier, bool isGuest, long totalPrice, int noShowCount,
        DecisionStatus expectedStatus, string? expectedRate, string expectedReason)
    {
        var history = DomainTestData.CreateHistory(noShowCount);
        var customer = isGuest ? DomainTestData.CreateGuest(history) : DomainTestData.CreateMember(tier, history);
        var reservation = DomainTestData.CreateReservation(customer: customer, totalPrice: totalPrice);

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(expectedStatus);
        decision.ReasonCode.Should().Be(expectedReason);
        decision.DepositRate.Should().Be(expectedRate is null ? null : decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
        decision.Reasons.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NoHistory_TreatsAsZeroNoShows()
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(CustomerTier.GOLD, history: null));

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(DecisionStatus.APPROVED);
        decision.DepositRate.Should().Be(0.3m);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NeedApproval_RecommendsManagerApproval()
    {
        var reservation = DomainTestData.CreateReservation(totalPrice: 2_500_000m);

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.Recommendations.Should().ContainSingle().Which.Type.Should().Be(RecommendationType.REQUIRE_APPROVAL);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_DifferentEvaluationTimes_ReturnsSameDecision()
    {
        var reservation = DomainTestData.CreateReservation(customer: DomainTestData.CreateMember(CustomerTier.SILVER));

        var early = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)), CancellationToken.None);
        var late = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(new DateTimeOffset(2035, 6, 1, 12, 0, 0, TimeSpan.Zero)), CancellationToken.None);

        late.Status.Should().Be(early.Status);
        late.DepositRate.Should().Be(early.DepositRate);
        late.ReasonCode.Should().Be(early.ReasonCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CustomHighValueThreshold_IsApplied()
    {
        // Ngưỡng tiền tệ chỉnh bằng cấu hình (PT-01 Hotel EUR) — không sửa mã
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { HighValueThreshold = 500m });
        var reservation = DomainTestData.CreateReservation(totalPrice: 600m, currency: "EUR");

        var decision = await strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.ReasonCode.Should().Be(ReasonCodes.HighValueReview);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Identity_NameAndVersion_MatchV2Contract()
    {
        Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        Strategy.StrategyVersion.Should().Be("2.0.0");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_DefaultOptions_EnabledForSixtyMinutes()
    {
        Strategy.CachePolicy.IsEnabled.Should().BeTrue();
        Strategy.CachePolicy.Ttl.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_ZeroTtl_IsDisabled()
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions { CacheTtlMinutes = 0 });

        strategy.CachePolicy.IsEnabled.Should().BeFalse();
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("StandardDepositRate")]
    [InlineData("SilverDepositRate")]
    [InlineData("GoldDepositRate")]
    [InlineData("PremiumDepositRate")]
    public void Validate_DepositRateWithMoreThanFourDecimals_Fails_Theory(string fieldName)
    {
        var options = CreateWithRate(fieldName, 0.33333m);

        var act = options.Validate;

        act.Should().Throw<ArgumentException>().WithMessage($"*{fieldName}*");
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("StandardDepositRate")]
    [InlineData("SilverDepositRate")]
    [InlineData("GoldDepositRate")]
    [InlineData("PremiumDepositRate")]
    public void Validate_DepositRateWithFourDecimals_Passes_Theory(string fieldName)
    {
        var options = CreateWithRate(fieldName, 0.3333m);

        var act = options.Validate;

        act.Should().NotThrow();
    }

    private static DefaultStrategyOptions CreateWithRate(string fieldName, decimal rate) => fieldName switch
    {
        "StandardDepositRate" => new DefaultStrategyOptions { StandardDepositRate = rate },
        "SilverDepositRate" => new DefaultStrategyOptions { SilverDepositRate = rate },
        "GoldDepositRate" => new DefaultStrategyOptions { GoldDepositRate = rate },
        "PremiumDepositRate" => new DefaultStrategyOptions { PremiumDepositRate = rate },
        _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null)
    };

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("rate-above-one")]
    [InlineData("negative-threshold")]
    [InlineData("zero-no-show-threshold")]
    [InlineData("negative-ttl")]
    public void Constructor_InvalidOptions_ThrowsArgumentException(string caseName)
    {
        var options = caseName switch
        {
            "rate-above-one" => new DefaultStrategyOptions { GoldDepositRate = 1.1m },
            "negative-threshold" => new DefaultStrategyOptions { HighValueThreshold = -1m },
            "zero-no-show-threshold" => new DefaultStrategyOptions { NoShowReviewThreshold = 0 },
            "negative-ttl" => new DefaultStrategyOptions { CacheTtlMinutes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var act = () => new DefaultDecisionStrategy(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => Strategy.EvaluateAsync(DomainTestData.CreateReservation(), DomainTestData.CreateContext(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
