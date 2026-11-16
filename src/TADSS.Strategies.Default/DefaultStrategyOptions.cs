using Shared.DecisionContractSDK.Enums;

namespace TADSS.Strategies.Default;

/// <summary>
/// Tham số của <see cref="DefaultDecisionStrategy"/> 2.2.0 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3),
/// runtime bind từ <c>Tadss:Strategies:Default</c>. Giá trị mặc định là chính sách SmashCourt; chính sách giảm cọc giờ thấp điểm
/// mặc định tắt nên khi không cấu hình gì strategy quyết định giống hệt 2.1.0.
/// </summary>
public sealed class DefaultStrategyOptions
{
    /// <summary>Số chữ số thập phân tối đa của tỷ lệ cọc (khớp giới hạn của runtime khi ghép response, C-09).</summary>
    private const int MaxRateDecimals = 4;

    /// <summary>Tổng tiền từ ngưỡng này trở lên cần Manager duyệt (đơn vị theo tiền tệ của request).</summary>
    public decimal HighValueThreshold { get; init; } = 2_000_000m;

    /// <summary>Số lần không đến từ ngưỡng này trở lên cần Manager duyệt (≥ 1).</summary>
    public int NoShowReviewThreshold { get; init; } = 2;

    /// <summary>
    /// Số lần hết hạn thanh toán (khách giữ chỗ nhưng không thanh toán) từ ngưỡng này trở lên, đồng thời nhiều hơn số lần hoàn tất, thì từ chối đơn.
    /// 0 = tắt quy tắc; ≥ 1 = bật. Là số lần vi phạm đã ghi nhận, không phải xác suất khách sẽ vi phạm tiếp.
    /// </summary>
    public int PaymentExpiryRejectThreshold { get; init; } = 5;

    /// <summary>Tỷ lệ cọc cho khách vãng lai và hạng <c>STANDARD</c>.</summary>
    public decimal StandardDepositRate { get; init; } = 0.5m;

    /// <summary>Tỷ lệ cọc cho hạng <c>SILVER</c>.</summary>
    public decimal SilverDepositRate { get; init; } = 0.4m;

    /// <summary>Tỷ lệ cọc cho hạng <c>GOLD</c>.</summary>
    public decimal GoldDepositRate { get; init; } = 0.3m;

    /// <summary>Tỷ lệ cọc cho hạng <c>PLATINUM</c> và <c>DIAMOND</c>.</summary>
    public decimal PremiumDepositRate { get; init; } = 0.2m;

    /// <summary>Thời gian sống của cache (phút); 0 = không cache.</summary>
    public int CacheTtlMinutes { get; init; } = 60;

    /// <summary>
    /// Bật chính sách giảm cọc giờ thấp điểm. Mặc định <c>false</c>: thiếu cấu hình thì không khách nào được giảm.
    /// Chỉ xét sau các quy tắc từ chối, duyệt đơn lớn, duyệt lịch sử không đến; không bao giờ đổi trạng thái quyết định.
    /// </summary>
    public bool OffPeakDiscountEnabled { get; init; }

    /// <summary>Tỷ lệ cọc tối đa khi được giảm: tỷ lệ áp dụng = min(tỷ lệ theo hạng, giá trị này).</summary>
    public decimal OffPeakDiscountRate { get; init; } = 0.3m;

    /// <summary>Hạng thành viên được giảm cọc giờ thấp điểm; khách vãng lai không bao giờ được giảm. Rỗng = không hạng nào.</summary>
    public List<CustomerTier> OffPeakDiscountTiers { get; init; } = [];

    /// <summary>Số lần không đến tối đa để được giảm (mặc định 0: chỉ khách chưa từng không đến).</summary>
    public int OffPeakDiscountMaxNoShowCount { get; init; }

    /// <summary>Số lần hết hạn thanh toán tối đa để được giảm (mặc định 0: chỉ khách chưa từng giữ chỗ rồi bỏ thanh toán).</summary>
    public int OffPeakDiscountMaxPaymentExpiredCount { get; init; }

    /// <summary>Tỷ lệ cọc theo hạng (khách vãng lai dùng tỷ lệ <c>STANDARD</c>).</summary>
    /// <param name="tier">Hạng thành viên.</param>
    /// <exception cref="ArgumentOutOfRangeException">Khi hạng không được hỗ trợ.</exception>
    public decimal TierRate(CustomerTier tier) => tier switch
    {
        CustomerTier.STANDARD => StandardDepositRate,
        CustomerTier.SILVER => SilverDepositRate,
        CustomerTier.GOLD => GoldDepositRate,
        CustomerTier.PLATINUM or CustomerTier.DIAMOND => PremiumDepositRate,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Hạng thành viên không được hỗ trợ.")
    };

    /// <summary>Tỷ lệ cọc giờ thấp điểm của một thành viên đủ điều kiện lịch sử: hạng trong danh sách thì min(tỷ lệ theo hạng, tỷ lệ giảm).</summary>
    /// <param name="tier">Hạng thành viên.</param>
    public decimal OffPeakRate(CustomerTier tier) =>
        OffPeakDiscountEnabled && OffPeakDiscountTiers.Contains(tier) ? Math.Min(TierRate(tier), OffPeakDiscountRate) : TierRate(tier);

    /// <summary>Kiểm tra tham số; ném <see cref="ArgumentException"/> khi không hợp lệ.</summary>
    public void Validate()
    {
        if (HighValueThreshold < 0m)
        {
            throw new ArgumentException("HighValueThreshold phải ≥ 0.", nameof(HighValueThreshold));
        }

        if (NoShowReviewThreshold < 1)
        {
            throw new ArgumentException("NoShowReviewThreshold phải ≥ 1.", nameof(NoShowReviewThreshold));
        }

        if (PaymentExpiryRejectThreshold < 0)
        {
            throw new ArgumentException("PaymentExpiryRejectThreshold phải ≥ 0 (0 = tắt).", nameof(PaymentExpiryRejectThreshold));
        }

        foreach (var (name, rate) in new[]
                 {
                     (nameof(StandardDepositRate), StandardDepositRate),
                     (nameof(SilverDepositRate), SilverDepositRate),
                     (nameof(GoldDepositRate), GoldDepositRate),
                     (nameof(PremiumDepositRate), PremiumDepositRate)
                 })
        {
            if (rate is < 0m or > 1m)
            {
                throw new ArgumentException($"{name} phải trong [0, 1].", name);
            }

            if (decimal.Round(rate, MaxRateDecimals) != rate)
            {
                throw new ArgumentException($"{name} không được có quá {MaxRateDecimals} chữ số thập phân.", name);
            }
        }

        if (CacheTtlMinutes < 0)
        {
            throw new ArgumentException("CacheTtlMinutes phải ≥ 0.", nameof(CacheTtlMinutes));
        }

        // Sau khi tỷ lệ theo hạng đã hợp lệ, để kiểm thứ tự cọc giữa các hạng dựa trên số đúng
        ValidateOffPeakDiscount();
    }

    // Tham số giảm cọc luôn được kiểm (kể cả khi tắt) để lỗi lộ ra ngay, không đợi tới lúc bật
    private void ValidateOffPeakDiscount()
    {
        if (OffPeakDiscountRate is < 0m or > 1m || decimal.Round(OffPeakDiscountRate, MaxRateDecimals) != OffPeakDiscountRate)
        {
            throw new ArgumentException($"OffPeakDiscountRate phải trong [0, 1] và không quá {MaxRateDecimals} chữ số thập phân.", nameof(OffPeakDiscountRate));
        }

        if (OffPeakDiscountMaxNoShowCount < 0)
        {
            throw new ArgumentException("OffPeakDiscountMaxNoShowCount phải ≥ 0.", nameof(OffPeakDiscountMaxNoShowCount));
        }

        if (OffPeakDiscountMaxPaymentExpiredCount < 0)
        {
            throw new ArgumentException("OffPeakDiscountMaxPaymentExpiredCount phải ≥ 0.", nameof(OffPeakDiscountMaxPaymentExpiredCount));
        }

        if (OffPeakDiscountTiers is null || OffPeakDiscountTiers.Any(tier => !Enum.IsDefined(tier)) || OffPeakDiscountTiers.Distinct().Count() != OffPeakDiscountTiers.Count)
        {
            throw new ArgumentException("OffPeakDiscountTiers chỉ được chứa hạng hợp lệ, không trùng.", nameof(OffPeakDiscountTiers));
        }

        if (!OffPeakDiscountEnabled)
        {
            return;
        }

        if (OffPeakDiscountTiers.Count == 0)
        {
            throw new ArgumentException("OffPeakDiscountEnabled = true nhưng OffPeakDiscountTiers rỗng.", nameof(OffPeakDiscountTiers));
        }

        // Giảm cọc không được làm một hạng thấp cọc ít hơn hạng cao hơn ở giờ thấp điểm (ví dụ chỉ giảm STANDARD xuống 25 % khi SILVER vẫn 40 %)
        var tiers = Enum.GetValues<CustomerTier>();
        for (var index = 1; index < tiers.Length; index++)
        {
            if (OffPeakRate(tiers[index]) > OffPeakRate(tiers[index - 1]))
            {
                throw new ArgumentException(
                    $"Giờ thấp điểm hạng {tiers[index]} cọc {OffPeakRate(tiers[index])} cao hơn hạng {tiers[index - 1]} ({OffPeakRate(tiers[index - 1])}): " +
                    "chỉnh OffPeakDiscountTiers hoặc OffPeakDiscountRate.",
                    nameof(OffPeakDiscountTiers));
            }
        }
    }
}
