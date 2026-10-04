namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Cấu hình giới hạn tốc độ theo client (token bucket, SEC-1), thuộc <see cref="SecurityOptions.RateLimiting"/> nên không có <c>SectionName</c> riêng.
/// Mỗi client (<c>ClientId</c> của API key) có một xô riêng, mặc định chứa 100 token và nạp lại 100 token mỗi giây. Giới hạn tính theo từng instance của ứng dụng.
/// </summary>
public class RateLimitingOptions
{
    /// <summary>Cận trên của <see cref="PermitLimit"/>.</summary>
    public const int MaxPermitLimit = 100_000;

    /// <summary>Cận trên của <see cref="ReplenishmentPeriodSeconds"/> (giây).</summary>
    public const int MaxReplenishmentPeriodSeconds = 3600;

    /// <summary>Dung lượng xô (số request tối đa dồn được, <c>1..100000</c>); cũng là số request liên tiếp tối đa một client gửi được khi xô đầy. Mặc định 100.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>Số token nạp lại mỗi <see cref="ReplenishmentPeriodSeconds"/> giây (<c>1..PermitLimit</c>). Mặc định 100.</summary>
    public int TokensPerPeriod { get; set; } = 100;

    /// <summary>Chu kỳ nạp token (giây, <c>1..3600</c>). Mặc định 1.</summary>
    public int ReplenishmentPeriodSeconds { get; set; } = 1;
}
