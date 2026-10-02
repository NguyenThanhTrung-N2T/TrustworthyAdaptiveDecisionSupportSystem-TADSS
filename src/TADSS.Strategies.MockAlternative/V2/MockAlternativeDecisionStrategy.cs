using System.Globalization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.Enums;
using Shared.DecisionContractSDK.V2.Interfaces;

namespace TADSS.Strategies.MockAlternative.V2;

/// <summary>
/// Strategy thay thế 2.0.0 — cọc theo khung giờ của chi nhánh (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3), dùng để
/// đối chứng với <c>DefaultDecisionStrategy</c>. Giờ cao điểm lấy từ lịch trong <see cref="DecisionContext"/>.
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class MockAlternativeDecisionStrategy : IReservationDecisionStrategy
{
    private readonly MockAlternativeStrategyOptions _options;

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public MockAlternativeDecisionStrategy(MockAlternativeStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        CachePolicy = options.CacheTtlMinutes > 0
            ? StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(options.CacheTtlMinutes))
            : StrategyCachePolicy.Disabled;
    }

    /// <inheritdoc />
    public string StrategyName => nameof(MockAlternativeDecisionStrategy);

    /// <inheritdoc />
    public string StrategyVersion => "2.0.0";

    /// <inheritdoc />
    public StrategyCachePolicy CachePolicy { get; }

    /// <inheritdoc />
    public Task<StrategyDecision> EvaluateAsync(Reservation reservation, DecisionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Decide(reservation, context.Calendar));
    }

    private StrategyDecision Decide(Reservation reservation, BranchCalendar calendar)
    {
        var isPeak = calendar.IsPeak(reservation);
        var slot = DescribeSlot(reservation.ToBranchLocal(reservation.EarliestStart));

        if (isPeak && reservation.TotalPrice >= _options.HighValueThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                [$"Đơn giá trị lớn ({FormatAmount(reservation.TotalPrice)} {reservation.Currency}) vào giờ cao điểm ({slot}, giờ chi nhánh)."],
                new[]
                {
                    new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét đơn giá trị lớn vào giờ cao điểm."),
                    new DecisionRecommendation(RecommendationType.ALTERNATIVE_SLOT, "Gợi ý khách chuyển sang khung giờ thấp điểm để được cọc thấp hơn.")
                });
        }

        return isPeak
            ? StrategyDecision.Approve(
                _options.PeakDepositRate,
                ReasonCodes.PeakHourDeposit,
                [$"Khung giờ cao điểm ({slot}, giờ chi nhánh) áp dụng cọc {FormatPercent(_options.PeakDepositRate)}."])
            : StrategyDecision.Approve(
                _options.OffPeakDepositRate,
                ReasonCodes.OffPeakDeposit,
                [$"Khung giờ thấp điểm ({slot}, giờ chi nhánh) áp dụng cọc {FormatPercent(_options.OffPeakDepositRate)}."]);
    }

    private static string DescribeSlot(DateTimeOffset branchLocal) =>
        $"{branchLocal.ToString("HH:mm", CultureInfo.InvariantCulture)} {VietnameseDayName(branchLocal.DayOfWeek)}";

    private static string VietnameseDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "thứ Hai",
        DayOfWeek.Tuesday => "thứ Ba",
        DayOfWeek.Wednesday => "thứ Tư",
        DayOfWeek.Thursday => "thứ Năm",
        DayOfWeek.Friday => "thứ Sáu",
        DayOfWeek.Saturday => "thứ Bảy",
        _ => "Chủ Nhật"
    };

    private static string FormatAmount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
