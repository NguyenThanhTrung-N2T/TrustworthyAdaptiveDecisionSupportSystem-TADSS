using System.Globalization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Strategies.Default;

/// <summary>
/// Strategy mặc định 2.0.0 — cọc theo hạng thành viên (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3).
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class DefaultDecisionStrategy : IReservationDecisionStrategy
{
    private readonly DefaultStrategyOptions _options;

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public DefaultDecisionStrategy(DefaultStrategyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _options = options;
        CachePolicy = options.CacheTtlMinutes > 0
            ? StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(options.CacheTtlMinutes))
            : StrategyCachePolicy.Disabled;
    }

    /// <inheritdoc />
    public string StrategyName => nameof(DefaultDecisionStrategy);

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

        return Task.FromResult(Decide(reservation));
    }

    private StrategyDecision Decide(Reservation reservation)
    {
        if (reservation.TotalPrice >= _options.HighValueThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.HighValueReview,
                [$"Giá trị đơn {FormatAmount(reservation.TotalPrice)} {reservation.Currency} đạt ngưỡng cần duyệt {FormatAmount(_options.HighValueThreshold)} {reservation.Currency}."],
                new[] { new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét đơn giá trị lớn và quyết định mức cọc.") });
        }

        var noShowCount = reservation.Customer.History?.NoShowCount ?? 0;
        if (noShowCount >= _options.NoShowReviewThreshold)
        {
            return StrategyDecision.RequireApproval(
                ReasonCodes.NoShowHistoryReview,
                [$"Khách có {noShowCount} lần không đến (ngưỡng {_options.NoShowReviewThreshold})."],
                new[] { new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, "Manager xem xét lịch sử không đến của khách và quyết định mức cọc.") });
        }

        var (rate, segment) = ResolveTierRate(reservation.Customer);
        return StrategyDecision.Approve(rate, ReasonCodes.TierDeposit, [$"Áp dụng cọc {FormatPercent(rate)} cho {segment}."]);
    }

    private (decimal Rate, string Segment) ResolveTierRate(CustomerProfile customer)
    {
        if (customer.IsGuest)
        {
            return (_options.StandardDepositRate, "khách vãng lai");
        }

        return customer.Tier switch
        {
            CustomerTier.STANDARD => (_options.StandardDepositRate, "hạng STANDARD"),
            CustomerTier.SILVER => (_options.SilverDepositRate, "hạng SILVER"),
            CustomerTier.GOLD => (_options.GoldDepositRate, "hạng GOLD"),
            CustomerTier.PLATINUM or CustomerTier.DIAMOND => (_options.PremiumDepositRate, $"hạng {customer.Tier}"),
            _ => throw new ArgumentOutOfRangeException(nameof(customer), customer.Tier, "Hạng thành viên không được hỗ trợ.")
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
