namespace TADSS.Strategies.MockAlternative;

/// <summary>
/// Tham số của <see cref="MockAlternativeDecisionStrategy"/> 2.0.0 (SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3),
/// runtime bind từ <c>Tadss:Strategies:MockAlternative</c>.
/// </summary>
public sealed class MockAlternativeStrategyOptions
{
    /// <summary>Số chữ số thập phân tối đa của tỷ lệ cọc (khớp giới hạn của runtime khi ghép response, C-09).</summary>
    private const int MaxRateDecimals = 4;

    /// <summary>Tổng tiền từ ngưỡng này trở lên vào giờ cao điểm cần Manager duyệt.</summary>
    public decimal HighValueThreshold { get; init; } = 1_500_000m;

    /// <summary>Tỷ lệ cọc giờ cao điểm.</summary>
    public decimal PeakDepositRate { get; init; } = 0.5m;

    /// <summary>Tỷ lệ cọc ngoài giờ cao điểm.</summary>
    public decimal OffPeakDepositRate { get; init; } = 0.3m;

    /// <summary>Thời gian sống của cache (phút); 0 = không cache.</summary>
    public int CacheTtlMinutes { get; init; } = 60;

    /// <summary>Kiểm tra tham số; ném <see cref="ArgumentException"/> khi không hợp lệ.</summary>
    public void Validate()
    {
        if (HighValueThreshold < 0m)
        {
            throw new ArgumentException("HighValueThreshold phải ≥ 0.", nameof(HighValueThreshold));
        }

        if (PeakDepositRate is < 0m or > 1m)
        {
            throw new ArgumentException("PeakDepositRate phải trong [0, 1].", nameof(PeakDepositRate));
        }

        if (OffPeakDepositRate is < 0m or > 1m)
        {
            throw new ArgumentException("OffPeakDepositRate phải trong [0, 1].", nameof(OffPeakDepositRate));
        }

        foreach (var (name, rate) in new[]
                 {
                     (nameof(PeakDepositRate), PeakDepositRate),
                     (nameof(OffPeakDepositRate), OffPeakDepositRate)
                 })
        {
            if (decimal.Round(rate, MaxRateDecimals) != rate)
            {
                throw new ArgumentException($"{name} không được có quá {MaxRateDecimals} chữ số thập phân.", name);
            }
        }

        if (CacheTtlMinutes < 0)
        {
            throw new ArgumentException("CacheTtlMinutes phải ≥ 0.", nameof(CacheTtlMinutes));
        }
    }
}
