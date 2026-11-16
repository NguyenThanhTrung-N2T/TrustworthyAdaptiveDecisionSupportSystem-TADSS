using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Strategies.Default;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Strategies;

/// <summary>
/// UT-03: chính sách giảm cọc giờ thấp điểm của <c>DefaultDecisionStrategy</c> 2.2.0 với cấu hình của bản deploy (STANDARD, SILVER; tối đa 30 %;
/// chưa từng không đến, chưa từng bỏ thanh toán) — giá trị biên của giờ (mốc bắt đầu cao điểm tính là cao điểm, mốc kết thúc không), cuối tuần theo lịch
/// cấu hình, lịch sử, giá trị đơn; giờ xét theo múi giờ chi nhánh, không theo offset của request hay múi giờ máy; mặc định tắt; lý do ghi mức cọc gốc,
/// mức sau giảm và dấu vân tay tham số.
/// </summary>
public class DefaultDecisionStrategyOffPeakDiscountTests
{
    internal const string Fingerprint = "0123456789abcdef";

    /// <summary>Cấu hình giảm cọc của bản deploy.</summary>
    internal static DefaultStrategyOptions DeployOptions(bool enabled = true) => new()
    {
        OffPeakDiscountEnabled = enabled,
        OffPeakDiscountRate = 0.3m,
        OffPeakDiscountTiers = [CustomerTier.STANDARD, CustomerTier.SILVER],
    };

    private static readonly DefaultDecisionStrategy Strategy = new(DeployOptions(), Fingerprint);

    private static Reservation Reservation(
        string start, CustomerTier? tier = CustomerTier.STANDARD, int noShow = 0, int expired = 0, int completed = 12,
        decimal price = 228_000m, TimeZoneInfo? timeZone = null)
    {
        var history = DomainTestData.CreateHistory(noShowCount: noShow, completedCount: completed, paymentExpiredCount: expired);
        var customer = tier is { } member ? DomainTestData.CreateMember(member, history) : DomainTestData.CreateGuest(history);
        var item = DomainTestData.CreateItem(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture));
        return DomainTestData.CreateReservation(items: [item], customer: customer, totalPrice: price, timeZone: timeZone);
    }

    /// <summary>Bảng biên: thứ Sáu 20/11/2026, thứ Bảy 21/11, Chủ Nhật 22/11 theo lịch chuẩn (T2–T6 17:00–21:00, T7–CN 08:00–21:00).</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.SILVER, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.GOLD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.PLATINUM, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.DIAMOND, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.2", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", null, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T16:59:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T17:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T20:59:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T21:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-21T07:59:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-21T08:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-22T20:59:00+07:00", CustomerTier.SILVER, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.4", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-22T21:00:00+07:00", CustomerTier.SILVER, 0, 0, 12, 228_000, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 1, 0, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 1, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 4, 12, 228_000, DecisionStatus.APPROVED, "0.5", ReasonCodes.TierDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 2, 0, 12, 228_000, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.NoShowHistoryReview)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 5, 0, 228_000, DecisionStatus.REJECTED, null, DefaultDecisionStrategy.RepeatedPaymentExpiryReasonCode)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 1_999_999, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.STANDARD, 0, 0, 12, 2_000_000, DecisionStatus.NEED_APPROVAL, null, ReasonCodes.HighValueReview)]
    [InlineData("2026-11-20T10:00:00+07:00", CustomerTier.SILVER, 0, 0, 0, 0, DecisionStatus.APPROVED, "0.3", ReasonCodes.OffPeakDeposit)]
    public async Task EvaluateAsync_DeployConfiguration_BoundaryTable(
        string start, CustomerTier? tier, int noShow, int expired, int completed, long price,
        DecisionStatus expectedStatus, string? expectedRate, string expectedReason)
    {
        var decision = await Strategy.EvaluateAsync(
            Reservation(start, tier, noShow, expired, completed, price), DomainTestData.CreateContext(), CancellationToken.None);

        decision.Status.Should().Be(expectedStatus);
        decision.ReasonCode.Should().Be(expectedReason);
        decision.DepositRate.Should().Be(expectedRate is null ? null : decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
    }

    /// <summary>Mặc định tắt: tham số mặc định (thiếu cấu hình) không giảm cọc cho ai, kể cả khi danh sách hạng đã khai báo.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_DiscountDisabled_KeepsTierRate()
    {
        var reservation = Reservation("2026-11-20T10:00:00+07:00");

        var byDefault = await new DefaultDecisionStrategy(new DefaultStrategyOptions()).EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);
        var disabled = await new DefaultDecisionStrategy(DeployOptions(enabled: false)).EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        byDefault.DepositRate.Should().Be(0.5m);
        byDefault.ReasonCode.Should().Be(ReasonCodes.TierDeposit);
        disabled.DepositRate.Should().Be(0.5m);
        disabled.ReasonCode.Should().Be(ReasonCodes.TierDeposit);
    }

    /// <summary>Khách chưa có lịch sử (tài khoản mới) có mọi bộ đếm bằng 0 nên đủ điều kiện — hạn chế đã biết, ghi trong báo cáo.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_NewCustomerWithoutHistory_IsEligible()
    {
        var item = DomainTestData.CreateItem(DateTimeOffset.Parse("2026-11-20T10:00:00+07:00", CultureInfo.InvariantCulture));
        var reservation = DomainTestData.CreateReservation(items: [item], customer: DomainTestData.CreateMember(CustomerTier.STANDARD, history: null));

        var decision = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        decision.DepositRate.Should().Be(0.3m);
        decision.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit);
    }

    /// <summary>Lý do ghi mức cọc gốc, mức sau giảm, giờ chi nhánh và dấu vân tay tham số; không có dấu vân tay thì không ghi phần cấu hình.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_Discounted_ReasonTracesBaseRateDiscountedRateAndFingerprint()
    {
        var reservation = Reservation("2026-11-20T10:00:00+07:00");

        var withFingerprint = await Strategy.EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);
        var withoutFingerprint = await new DefaultDecisionStrategy(DeployOptions()).EvaluateAsync(reservation, DomainTestData.CreateContext(), CancellationToken.None);

        withFingerprint.Reasons.Should().ContainSingle().Which.Should()
            .Contain("50% -> 30%").And.Contain("20/11/2026 10:00").And.Contain("hạng STANDARD").And.EndWith($"cấu hình strategy {Fingerprint}.");
        withoutFingerprint.Reasons.Should().ContainSingle().Which.Should().Contain("50% -> 30%").And.NotContain("cấu hình strategy");
    }

    /// <summary>
    /// Giờ cao điểm xét theo giờ chi nhánh của thời điểm bắt đầu: cùng một thời điểm viết bằng offset khác (UTC hay +07:00) cho cùng kết quả, nên múi giờ
    /// của máy chạy hay của client không đổi được quyết định; cùng thời điểm ở chi nhánh Lisbon (đang là 18:30 thứ Sáu, cao điểm) thì không được giảm.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData("2026-11-20T09:59:00+00:00", null, "0.3")]
    [InlineData("2026-11-20T16:59:00+07:00", null, "0.3")]
    [InlineData("2026-11-20T10:00:00+00:00", null, "0.5")]
    [InlineData("2026-11-20T17:00:00+07:00", null, "0.5")]
    [InlineData("2026-11-21T01:30:00+07:00", null, "0.3")]
    [InlineData("2026-11-21T01:30:00+07:00", "Europe/Lisbon", "0.5")]
    public async Task EvaluateAsync_PeakCheck_UsesBranchTimeZoneOfTheInstant(string start, string? branchTimeZone, string expectedRate)
    {
        var timeZone = branchTimeZone is null ? null : TimeZoneInfo.FindSystemTimeZoneById(branchTimeZone);

        var decision = await Strategy.EvaluateAsync(Reservation(start, timeZone: timeZone), DomainTestData.CreateContext(), CancellationToken.None);

        decision.DepositRate.Should().Be(decimal.Parse(expectedRate, CultureInfo.InvariantCulture));
    }

    /// <summary>Tỷ lệ áp dụng là min(theo hạng, tỷ lệ giảm): tỷ lệ giảm cao hơn tỷ lệ theo hạng thì không đổi và giữ lý do cọc theo hạng.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public async Task EvaluateAsync_DiscountRateAboveTierRate_KeepsTierRate()
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions
        {
            OffPeakDiscountEnabled = true,
            OffPeakDiscountRate = 0.45m,
            OffPeakDiscountTiers = [CustomerTier.STANDARD, CustomerTier.SILVER],
        });

        var standard = await strategy.EvaluateAsync(Reservation("2026-11-20T10:00:00+07:00"), DomainTestData.CreateContext(), CancellationToken.None);
        var silver = await strategy.EvaluateAsync(Reservation("2026-11-20T10:00:00+07:00", CustomerTier.SILVER), DomainTestData.CreateContext(), CancellationToken.None);

        standard.DepositRate.Should().Be(0.45m);
        standard.ReasonCode.Should().Be(ReasonCodes.OffPeakDeposit);
        silver.DepositRate.Should().Be(0.4m);
        silver.ReasonCode.Should().Be(ReasonCodes.TierDeposit);
    }

    /// <summary>Ngưỡng lịch sử cấu hình được: nới lên 1 thì khách có đúng một lần không đến hoặc bỏ thanh toán được giảm (50 % xuống 40 %), hai lần thì không.</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [InlineData(1, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 2, false)]
    public async Task EvaluateAsync_RelaxedHistoryThresholds_AreApplied(int noShow, int expired, bool discounted)
    {
        var strategy = new DefaultDecisionStrategy(new DefaultStrategyOptions
        {
            OffPeakDiscountEnabled = true,
            OffPeakDiscountTiers = [CustomerTier.STANDARD],
            OffPeakDiscountRate = 0.4m,
            OffPeakDiscountMaxNoShowCount = 1,
            OffPeakDiscountMaxPaymentExpiredCount = 1,
        });

        var decision = await strategy.EvaluateAsync(
            Reservation("2026-11-20T10:00:00+07:00", noShow: noShow, expired: expired), DomainTestData.CreateContext(), CancellationToken.None);

        decision.DepositRate.Should().Be(discounted ? 0.4m : 0.5m);
    }

    /// <summary>Cấu hình sai làm strategy không dựng được (runtime không khởi động, fail-fast), kể cả khi chính sách đang tắt.</summary>
    [Theory]
    [Trait("CatalogId", "UT-03")]
    [MemberData(nameof(InvalidOptions))]
    public void Constructor_InvalidDiscountOptions_Throws(DefaultStrategyOptions options, string parameter)
    {
        var act = () => new DefaultDecisionStrategy(options);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(parameter);
    }

    /// <summary>Các cấu hình sai: tỷ lệ ngoài [0, 1] hoặc quá 4 chữ số thập phân, ngưỡng âm, hạng lạ hoặc trùng, bật mà không có hạng, hạng thấp cọc ít hơn hạng cao.</summary>
    public static TheoryData<DefaultStrategyOptions, string> InvalidOptions() => new()
    {
        { new DefaultStrategyOptions { OffPeakDiscountRate = 1.1m }, nameof(DefaultStrategyOptions.OffPeakDiscountRate) },
        { new DefaultStrategyOptions { OffPeakDiscountRate = -0.1m }, nameof(DefaultStrategyOptions.OffPeakDiscountRate) },
        { new DefaultStrategyOptions { OffPeakDiscountRate = 0.12345m }, nameof(DefaultStrategyOptions.OffPeakDiscountRate) },
        { new DefaultStrategyOptions { OffPeakDiscountMaxNoShowCount = -1 }, nameof(DefaultStrategyOptions.OffPeakDiscountMaxNoShowCount) },
        { new DefaultStrategyOptions { OffPeakDiscountMaxPaymentExpiredCount = -1 }, nameof(DefaultStrategyOptions.OffPeakDiscountMaxPaymentExpiredCount) },
        { new DefaultStrategyOptions { OffPeakDiscountTiers = [(CustomerTier)99] }, nameof(DefaultStrategyOptions.OffPeakDiscountTiers) },
        { new DefaultStrategyOptions { OffPeakDiscountTiers = [CustomerTier.SILVER, CustomerTier.SILVER] }, nameof(DefaultStrategyOptions.OffPeakDiscountTiers) },
        { new DefaultStrategyOptions { OffPeakDiscountEnabled = true }, nameof(DefaultStrategyOptions.OffPeakDiscountTiers) },
        {
            new DefaultStrategyOptions { OffPeakDiscountEnabled = true, OffPeakDiscountRate = 0.25m, OffPeakDiscountTiers = [CustomerTier.STANDARD] },
            nameof(DefaultStrategyOptions.OffPeakDiscountTiers)
        },
    };

    /// <summary>Cấu hình hợp lệ dựng được: cấu hình deploy, chỉ khai báo hạng khi đang tắt, giảm cả năm hạng về một mức.</summary>
    [Fact]
    [Trait("CatalogId", "UT-03")]
    public void Constructor_ValidDiscountOptions_DoesNotThrow()
    {
        var all = Enum.GetValues<CustomerTier>().ToList();

        FluentActions.Invoking(() => new DefaultDecisionStrategy(DeployOptions())).Should().NotThrow();
        FluentActions.Invoking(() => new DefaultDecisionStrategy(DeployOptions(enabled: false))).Should().NotThrow();
        FluentActions.Invoking(() => new DefaultDecisionStrategy(new DefaultStrategyOptions { OffPeakDiscountEnabled = true, OffPeakDiscountRate = 0.1m, OffPeakDiscountTiers = all }))
            .Should().NotThrow();
    }
}
