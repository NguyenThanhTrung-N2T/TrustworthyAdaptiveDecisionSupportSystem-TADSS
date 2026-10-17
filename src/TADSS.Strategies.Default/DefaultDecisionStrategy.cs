using System.Globalization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Strategies.Default;

/// <summary>
/// Strategy mặc định 2.1.0 — cọc theo hạng thành viên, duyệt đơn lớn hoặc khách hay không đến, từ chối khách lặp lại việc giữ chỗ mà không thanh toán
/// (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3).
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class DefaultDecisionStrategy : IReservationDecisionStrategy
{
    /// <summary>Mã lý do của quyết định từ chối khách lặp lại việc giữ chỗ nhưng để hết hạn thanh toán (contract cho phép mã mới, INTEGRATION_SPEC Mục 6).</summary>
    public const string RepeatedPaymentExpiryReasonCode = "REPEATED_PAYMENT_EXPIRY";

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
    public string StrategyVersion => "2.1.0";

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
        // Từ chối xét đầu tiên: đưa khách hay giữ chỗ vào hàng chờ duyệt chỉ kéo dài thời gian slot bị giữ mà không ai thanh toán
        var history = reservation.Customer.History;
        var paymentExpired = history?.PaymentExpiredCount ?? 0;
        if (_options.PaymentExpiryRejectThreshold > 0
            && paymentExpired >= _options.PaymentExpiryRejectThreshold
            && paymentExpired > history!.CompletedCount)
        {
            return StrategyDecision.Reject(
                RepeatedPaymentExpiryReasonCode,
                [$"Khách có {paymentExpired} lần giữ chỗ hết hạn thanh toán và {history.CompletedCount} lần hoàn tất (ngưỡng {_options.PaymentExpiryRejectThreshold})."]);
        }

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
