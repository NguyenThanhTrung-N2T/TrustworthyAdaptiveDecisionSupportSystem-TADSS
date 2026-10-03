namespace TADSS.Application.Common;

/// <summary>
/// Chuẩn hóa mốc thời gian ghi vào audit (<c>timestamptz</c>): đánh giá, override và outcome dùng chung một cách cắt
/// để giá trị đọc lại từ PostgreSQL trùng từng tick với giá trị đã trả.
/// </summary>
public static class AuditTimestamps
{
    /// <summary>
    /// Cắt phần dưới micro giây (bội số 10 tick) để mốc khớp độ chính xác của cột <c>timestamptz</c>
    /// và response replay/audit trùng từng tick với response gốc (B-03, C-08).
    /// </summary>
    /// <param name="value">Mốc thời gian cần cắt.</param>
    /// <returns>Mốc đã cắt, offset 0 (UTC).</returns>
    public static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }
}
