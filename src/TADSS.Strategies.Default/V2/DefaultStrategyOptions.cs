namespace TADSS.Strategies.Default.V2;

/// <summary>
/// Tham số của <see cref="DefaultDecisionStrategy"/> 2.0.0 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3),
/// runtime bind từ <c>Tadss:Strategies:Default</c>. Giá trị mặc định là chính sách SmashCourt.
/// </summary>
public sealed class DefaultStrategyOptions
{
    /// <summary>Tổng tiền từ ngưỡng này trở lên cần Manager duyệt (đơn vị theo tiền tệ của request).</summary>
    public decimal HighValueThreshold { get; init; } = 2_000_000m;

    /// <summary>Số lần không đến từ ngưỡng này trở lên cần Manager duyệt (≥ 1).</summary>
    public int NoShowReviewThreshold { get; init; } = 2;

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
        }

        if (CacheTtlMinutes < 0)
        {
            throw new ArgumentException("CacheTtlMinutes phải ≥ 0.", nameof(CacheTtlMinutes));
        }
    }
}
