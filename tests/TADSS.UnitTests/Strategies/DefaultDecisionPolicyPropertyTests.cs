using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03: tính chất của chính sách cọc <c>DefaultDecisionStrategy</c> 2.2.0, kiểm trên toàn bộ tổ hợp đại diện của khách (vãng lai, 5 hạng) × giờ bắt đầu
/// (biên cao điểm ngày thường và cuối tuần) × số lần không đến × (bỏ thanh toán, đã chơi) × giá trị đơn. "Bản gốc" là cùng tham số nhưng tắt giảm cọc,
/// tức hành vi của 2.1.0 (bảng quyết định 2.1.0 ở <c>DefaultDecisionStrategyTests</c>/<c>RejectionTests</c> vẫn đạt nguyên vẹn).
/// Hai cấu hình: của bản deploy và một cấu hình khác (chỉ STANDARD, 40 %, nới ngưỡng lịch sử lên 1) để tính chất không phụ thuộc một bộ số.
/// </summary>
public class DefaultDecisionPolicyPropertyTests
{
    private static readonly string[] Starts =
    [
        "2026-11-20T10:00:00+07:00", "2026-11-20T16:59:00+07:00", "2026-11-20T17:00:00+07:00", "2026-11-20T20:59:00+07:00",
        "2026-11-20T21:00:00+07:00", "2026-11-21T07:59:00+07:00", "2026-11-21T08:00:00+07:00", "2026-11-21T20:59:00+07:00",
        "2026-11-22T12:00:00+07:00", "2026-11-22T21:00:00+07:00",
    ];

    private static readonly CustomerTier?[] Customers = [null, CustomerTier.STANDARD, CustomerTier.SILVER, CustomerTier.GOLD, CustomerTier.PLATINUM, CustomerTier.DIAMOND];
    private static readonly int[] NoShows = [0, 1, 2, 3];
    private static readonly (int Expired, int Completed)[] PaymentHistories = [(0, 12), (1, 12), (4, 12), (5, 12), (6, 12), (0, 0), (4, 0), (5, 0), (6, 0)];
    private static readonly decimal[] Prices = [0m, 228_000m, 1_999_999m, 2_000_000m, 2_000_001m];

    /// <summary>Tên cấu hình được kiểm.</summary>
    public static TheoryData<string> Configurations() => new() { "deploy", "alternative" };

    private static DefaultStrategyOptions Options(string configuration, bool enabled) => configuration switch
    {
        "deploy" => DefaultDecisionStrategyOffPeakDiscountTests.DeployOptions(enabled),
        _ => new DefaultStrategyOptions
        {
            OffPeakDiscountEnabled = enabled,
            OffPeakDiscountRate = 0.4m,
            OffPeakDiscountTiers = [CustomerTier.STANDARD],
            OffPeakDiscountMaxNoShowCount = 1,
            OffPeakDiscountMaxPaymentExpiredCount = 1,
        },
    };

    private sealed record Case(CustomerTier? Tier, string Start, int NoShow, int Expired, int Completed, decimal Price)
    {
        public bool IsGuest => Tier is null;
    }

    private sealed record Outcome(Case Case, bool IsPeak, StrategyDecision Baseline, StrategyDecision Candidate)
    {
        public bool Discounted => Candidate.DepositRate < Baseline.DepositRate;
    }

    private static async Task<List<Outcome>> EvaluateAllAsync(string configuration)
    {
        var baseline = new DefaultDecisionStrategy(Options(configuration, enabled: false));
        var candidate = new DefaultDecisionStrategy(Options(configuration, enabled: true));
        var context = DomainTestData.CreateContext();
        var outcomes = new List<Outcome>();
        foreach (var tier in Customers)
        foreach (var start in Starts)
        foreach (var noShow in NoShows)
        foreach (var (expired, completed) in PaymentHistories)
        foreach (var price in Prices)
        {
            var history = DomainTestData.CreateHistory(noShowCount: noShow, completedCount: completed, paymentExpiredCount: expired);
            var customer = tier is { } member ? DomainTestData.CreateMember(member, history) : DomainTestData.CreateGuest(history);
            var item = DomainTestData.CreateItem(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture));
            var reservation = DomainTestData.CreateReservation(items: [item], customer: customer, totalPrice: price);
            outcomes.Add(new Outcome(
                new Case(tier, start, noShow, expired, completed, price),
                context.Calendar.IsPeak(reservation),
                await baseline.EvaluateAsync(reservation, context, CancellationToken.None),
                await candidate.EvaluateAsync(reservation, context, CancellationToken.None)));
        }

        return outcomes;
    }

    /// <summary>Trạng thái quyết định không bao giờ đổi; từ chối và chờ duyệt giống hệt bản gốc (mã lý do, lý do, khuyến nghị).</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_StatusAndNonApprovedDecisions_AreIdenticalToBaseline(string configuration)
    {
        foreach (var outcome in await EvaluateAllAsync(configuration))
        {
            outcome.Candidate.Status.Should().Be(outcome.Baseline.Status, outcome.Case.ToString());
            if (outcome.Baseline.Status != DecisionStatus.APPROVED)
            {
                outcome.Candidate.Should().BeEquivalentTo(outcome.Baseline, outcome.Case.ToString());
            }
        }
    }

    /// <summary>
    /// Mức cọc không bao giờ cao hơn bản gốc; chỉ nhóm đủ điều kiện (thành viên thuộc hạng được giảm, ngoài giờ cao điểm, lịch sử trong ngưỡng, đơn dưới
    /// ngưỡng duyệt) mới có thể khác, khác thì mã lý do là giảm cọc giờ thấp điểm và tỷ lệ đúng bằng min(theo hạng, tỷ lệ giảm); ngoài nhóm đó quyết định giống hệt.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_OnlyEligibleCasesChange_AndNeverIncreaseDeposit(string configuration)
    {
        var options = Options(configuration, enabled: true);
        foreach (var outcome in await EvaluateAllAsync(configuration))
        {
            var (c, because) = (outcome.Case, outcome.Case.ToString());
            if (outcome.Baseline.Status != DecisionStatus.APPROVED)
            {
                continue;
            }

            outcome.Candidate.DepositRate.Should().BeLessThanOrEqualTo(outcome.Baseline.DepositRate!.Value, because);
            var eligible = !c.IsGuest && options.OffPeakDiscountTiers.Contains(c.Tier!.Value) && !outcome.IsPeak
                && c.NoShow <= options.OffPeakDiscountMaxNoShowCount && c.Expired <= options.OffPeakDiscountMaxPaymentExpiredCount
                && c.Price < options.HighValueThreshold;
            if (outcome.Discounted)
            {
                eligible.Should().BeTrue(because);
                outcome.Candidate.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit, because);
            }
            else
            {
                outcome.Candidate.Should().BeEquivalentTo(outcome.Baseline, because);
            }

            if (eligible)
            {
                outcome.Candidate.DepositRate.Should().Be(Math.Min(options.TierRate(c.Tier!.Value), options.OffPeakDiscountRate), because);
            }
        }
    }

    /// <summary>Đơn từ 2.000.000 đ luôn chờ Manager duyệt (trừ khi đã bị từ chối ở bước trước), ở mọi giờ và mọi hạng.</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_HighValueOrders_AlwaysNeedApproval(string configuration)
    {
        foreach (var outcome in (await EvaluateAllAsync(configuration)).Where(o => o.Case.Price >= 2_000_000m))
        {
            if (outcome.Candidate.Status != DecisionStatus.REJECTED)
            {
                outcome.Candidate.Status.Should().Be(DecisionStatus.NEED_APPROVAL, outcome.Case.ToString());
                outcome.Candidate.ReasonCode.Should().Be(ReasonCodes.HighValueReview, outcome.Case.ToString());
            }
        }
    }

    /// <summary>Cùng giờ, lịch sử, giá trị đơn: hạng cao hơn không bao giờ cọc nhiều hơn hạng thấp hơn (so các thành viên đều được duyệt cọc).</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_HigherTier_NeverPaysHigherDeposit(string configuration)
    {
        var groups = (await EvaluateAllAsync(configuration))
            .Where(o => !o.Case.IsGuest && o.Candidate.Status == DecisionStatus.APPROVED)
            .GroupBy(o => (o.Case.Start, o.Case.NoShow, o.Case.Expired, o.Case.Completed, o.Case.Price));
        foreach (var group in groups)
        {
            var rates = group.OrderBy(o => o.Case.Tier).Select(o => o.Candidate.DepositRate!.Value).ToList();
            rates.Should().BeInDescendingOrder(group.Key.ToString());
        }
    }

    /// <summary>Cùng khách, lịch sử, giá trị đơn: mọi mức cọc giờ cao điểm đều ≥ mọi mức cọc giờ thấp điểm.</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_PeakHour_NeverCheaperThanOffPeak(string configuration)
    {
        var groups = (await EvaluateAllAsync(configuration))
            .Where(o => o.Candidate.Status == DecisionStatus.APPROVED)
            .GroupBy(o => (o.Case.Tier, o.Case.NoShow, o.Case.Expired, o.Case.Completed, o.Case.Price));
        foreach (var group in groups)
        {
            var peak = group.Where(o => o.IsPeak).Select(o => o.Candidate.DepositRate!.Value).ToList();
            var offPeak = group.Where(o => !o.IsPeak).Select(o => o.Candidate.DepositRate!.Value).ToList();
            if (peak.Count > 0 && offPeak.Count > 0)
            {
                peak.Min().Should().BeGreaterThanOrEqualTo(offPeak.Max(), group.Key.ToString());
            }
        }
    }

    /// <summary>
    /// Tăng dấu hiệu rủi ro (số lần không đến, số lần bỏ thanh toán) không bao giờ đưa khách vào nhánh giảm cọc: tập trường hợp được giảm đóng theo chiều
    /// giảm rủi ro — trường hợp nào được giảm thì mọi trường hợp cùng điều kiện mà rủi ro thấp hơn hoặc bằng cũng được giảm.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(Configurations))]
    public async Task Policy_MoreRisk_NeverEntersDiscount(string configuration)
    {
        var outcomes = await EvaluateAllAsync(configuration);
        var discounted = outcomes.Where(o => o.Discounted).Select(o => o.Case).ToHashSet();
        foreach (var riskier in outcomes.Where(o => o.Discounted).Select(o => o.Case))
        {
            foreach (var safer in outcomes.Select(o => o.Case).Where(c =>
                         c.Tier == riskier.Tier && c.Start == riskier.Start && c.Price == riskier.Price && c.Completed == riskier.Completed
                         && c.NoShow <= riskier.NoShow && c.Expired <= riskier.Expired))
            {
                discounted.Should().Contain(safer, $"{riskier} được giảm nên {safer} (ít rủi ro hơn) cũng phải được giảm");
            }
        }

        discounted.Should().NotBeEmpty("tập tổ hợp phải có trường hợp được giảm để các tính chất trên có nghĩa");
    }
}
