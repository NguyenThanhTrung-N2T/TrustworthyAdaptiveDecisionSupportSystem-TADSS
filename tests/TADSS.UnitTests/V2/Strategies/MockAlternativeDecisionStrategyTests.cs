using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.Strategies.MockAlternative.V2;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Strategies;

/// <summary>
/// UT-03 / UT-05: bảng quyết định <c>MockAlternativeDecisionStrategy</c> 2.0.0 theo giờ cao điểm của chi nhánh
/// (sửa lỗi v1 so sánh trên giờ UTC), ngưỡng giá trị lớn chỉ áp dụng giờ cao điểm, lịch lấy từ ngữ cảnh.
/// </summary>
public class MockAlternativeDecisionStrategyTests
{
    private static readonly MockAlternativeDecisionStrategy Strategy = new(new MockAlternativeStrategyOptions());

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("2026-11-20T18:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-20T16:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T18:00:00+07:00", 1_500_000, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    [InlineData("2026-11-20T18:00:00+07:00", 1_499_999, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-20T16:00:00+07:00", 5_000_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T11:00:00Z", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    [InlineData("2026-11-21T09:00:00+07:00", 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.PeakHourDeposit)]
    public async Task EvaluateAsync_DecisionTable_ReturnsExpectedDecision(
        string startIso, long totalPrice, DecisionStatus expectedStatus, string? expectedRate, string expectedReason)
    {
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(items: [V2TestData.CreateItem(start)], totalPrice: totalPrice);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(expectedStatus);
        decision.ReasonCode.Should().Be(expectedReason);
        decision.DepositRate.Should().Be(expectedRate is null ? null : decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public async Task EvaluateAsync_PeakReason_UsesBranchLocalTimeAndVietnameseDay()
    {
        var reservation = V2TestData.CreateReservation(
            items: [V2TestData.CreateItem(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero))]);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Reasons.Should().ContainSingle()
            .Which.Should().Be("Khung giờ cao điểm (18:00 thứ Sáu, giờ chi nhánh) áp dụng cọc 50%.");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_HighValueAtPeak_RecommendsApprovalAndAlternativeSlot()
    {
        var reservation = V2TestData.CreateReservation(totalPrice: 2_000_000m);

        var decision = await Strategy.EvaluateAsync(reservation, V2TestData.CreateContext(), CancellationToken.None);

        decision.Recommendations.Select(recommendation => recommendation.Type).Should().Equal(
            RecommendationType.REQUIRE_APPROVAL, RecommendationType.ALTERNATIVE_SLOT);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public async Task EvaluateAsync_CalendarWithoutPeakWindows_TreatsAsOffPeak()
    {
        var context = V2TestData.CreateContext(calendar: new BranchCalendar([]));

        var decision = await Strategy.EvaluateAsync(V2TestData.CreateReservation(), context, CancellationToken.None);

        decision.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit);
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Identity_NameAndVersion_MatchV2Contract()
    {
        Strategy.StrategyName.Should().Be("MockAlternativeDecisionStrategy");
        Strategy.StrategyVersion.Should().Be("2.0.0");
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void CachePolicy_DefaultOptions_EnabledForSixtyMinutes()
    {
        Strategy.CachePolicy.IsEnabled.Should().BeTrue();
        Strategy.CachePolicy.Ttl.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("peak-rate-above-one")]
    [InlineData("negative-threshold")]
    [InlineData("negative-ttl")]
    public void Constructor_InvalidOptions_ThrowsArgumentException(string caseName)
    {
        var options = caseName switch
        {
            "peak-rate-above-one" => new MockAlternativeStrategyOptions { PeakDepositRate = 1.5m },
            "negative-threshold" => new MockAlternativeStrategyOptions { HighValueThreshold = -1m },
            "negative-ttl" => new MockAlternativeStrategyOptions { CacheTtlMinutes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        var act = () => new MockAlternativeDecisionStrategy(options);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => Strategy.EvaluateAsync(V2TestData.CreateReservation(), V2TestData.CreateContext(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
