using System.Globalization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Strategies.Default;

/// <summary>
/// Strategy mặc định 2.2.0 — cọc theo hạng thành viên, duyệt đơn lớn hoặc khách hay không đến, từ chối khách lặp lại việc giữ chỗ mà không thanh toán
/// (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3); thêm chính sách giảm cọc giờ thấp điểm (mặc định tắt, tắt thì quyết định giống hệt 2.1.0).
/// Thứ tự xét: từ chối ⇒ duyệt đơn lớn ⇒ duyệt lịch sử không đến ⇒ giảm cọc giờ thấp điểm ⇒ cọc theo hạng. Bước giảm cọc chỉ hạ tỷ lệ của
/// một quyết định đã là <c>APPROVED</c>, không bao giờ đổi trạng thái hay ghi đè ba bước trước.
/// Không đọc đồng hồ hệ thống, không I/O, stateless và thread-safe.
/// </summary>
public sealed class DefaultDecisionStrategy : IReservationDecisionStrategy
{
    /// <summary>Mã lý do của quyết định từ chối khách lặp lại việc giữ chỗ nhưng để hết hạn thanh toán (contract cho phép mã mới, INTEGRATION_SPEC Mục 6).</summary>
    public const string RepeatedPaymentExpiryReasonCode = "REPEATED_PAYMENT_EXPIRY";

    private readonly DefaultStrategyOptions _options;
    private readonly string _optionsFingerprint;

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra, không kèm dấu vân tay tham số.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public DefaultDecisionStrategy(DefaultStrategyOptions options)
        : this(options, string.Empty)
    {
    }

    /// <summary>Khởi tạo strategy với tham số đã kiểm tra và dấu vân tay tham số do runtime tính.</summary>
    /// <param name="options">Tham số strategy.</param>
    /// <param name="optionsFingerprint">Dấu vân tay tham số (cùng giá trị runtime dùng cho khóa cache); ghi vào lý do của quyết định được giảm cọc để truy vết cấu hình. Rỗng = không ghi.</param>
    /// <exception cref="ArgumentException">Khi tham số không hợp lệ.</exception>
    public DefaultDecisionStrategy(DefaultStrategyOptions options, string optionsFingerprint)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(optionsFingerprint);
        options.Validate();

        _options = options;
        _optionsFingerprint = optionsFingerprint;
        CachePolicy = options.CacheTtlMinutes > 0
            ? StrategyCachePolicy.Enabled(TimeSpan.FromMinutes(options.CacheTtlMinutes))
            : StrategyCachePolicy.Disabled;
    }

    /// <inheritdoc />
    public string StrategyName => nameof(DefaultDecisionStrategy);

    /// <inheritdoc />
    public string StrategyVersion => "2.2.0";

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
        if (IsOffPeakDiscountEligible(reservation, calendar))
        {
            var discounted = _options.OffPeakRate(reservation.Customer.Tier);
            if (discounted < rate)
            {
                var slot = reservation.ToBranchLocal(reservation.EarliestStart).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                var trace = _optionsFingerprint.Length > 0 ? $"; cấu hình strategy {_optionsFingerprint}" : string.Empty;
                return StrategyDecision.Approve(
                    discounted,
                    ReasonCodes.OffPeakDeposit,
                    [$"Giảm cọc giờ thấp điểm ({slot}, giờ chi nhánh) cho {segment} chưa có lịch sử rủi ro: {FormatPercent(rate)} -> {FormatPercent(discounted)}{trace}."]);
            }
        }

        return StrategyDecision.Approve(rate, ReasonCodes.TierDeposit, [$"Áp dụng cọc {FormatPercent(rate)} cho {segment}."]);
    }

    // Chỉ thành viên thuộc hạng được giảm, đặt ngoài giờ cao điểm (giờ bắt đầu sớm nhất, theo giờ chi nhánh) và lịch sử trong ngưỡng.
    // Khách chưa có lịch sử có mọi bộ đếm bằng 0 nên đủ điều kiện (hạn chế đã biết: tài khoản mới chưa có dữ liệu để đánh giá).
    private bool IsOffPeakDiscountEligible(Reservation reservation, BranchCalendar calendar)
    {
        var customer = reservation.Customer;
        if (!_options.OffPeakDiscountEnabled || customer.IsGuest || !_options.OffPeakDiscountTiers.Contains(customer.Tier))
        {
            return false;
        }

        var history = customer.History;
        return (history?.NoShowCount ?? 0) <= _options.OffPeakDiscountMaxNoShowCount
            && (history?.PaymentExpiredCount ?? 0) <= _options.OffPeakDiscountMaxPaymentExpiredCount
            && !calendar.IsPeak(reservation);
    }

    private (decimal Rate, string Segment) ResolveTierRate(CustomerProfile customer)
    {
        if (customer.IsGuest)
        {
            return (_options.StandardDepositRate, "khách vãng lai");
        }

        return (_options.TierRate(customer.Tier), $"hạng {customer.Tier}");
    }

    private static string FormatAmount(decimal amount) => amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string FormatPercent(decimal rate) => (rate * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
