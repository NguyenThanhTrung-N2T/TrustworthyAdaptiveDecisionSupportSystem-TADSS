namespace TADSS.Infrastructure.Options;

/// <summary>
/// Cấu hình cho tầng Caching phân tán (Redis + In-Memory Fallback) của TADSS.
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Cache";

    /// <summary>
    /// Nhà cung cấp cache chính: <see cref="CacheProviders.Redis"/> hoặc <see cref="CacheProviders.Memory"/> (không phân biệt hoa thường). Mặc định "Redis".
    /// </summary>
    public string Provider { get; set; } = CacheProviders.Redis;

    /// <summary>
    /// Connection string cho Redis instance (ví dụ "localhost:6379").
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Tiền tố namespace trong Redis (ví dụ "tadss:"). Hiện được bind nhưng không có mã nào đọc nên không có tác dụng:
    /// tiền tố khóa cache là hằng của <c>StrategyDecisionCacheKey</c>.
    /// </summary>
    public string InstanceName { get; set; } = "tadss:";

    /// <summary>
    /// Thời gian sống mặc định của cache (tính bằng giờ). Mặc định 24h. Hiện được bind và kiểm phải dương nhưng không có mã nào đọc nên
    /// không có tác dụng: TTL lấy từ tham số của lời gọi cache.
    /// </summary>
    public int TtlHours { get; set; } = 24;

    /// <summary>
    /// Cho phép tự động fallback sang In-Memory Cache khi Redis mất kết nối hoặc timeout.
    /// </summary>
    public bool EnableFallbackToMemory { get; set; } = true;

    /// <summary>
    /// Timeout tối đa cho thao tác Redis (ms) trước khi chuyển sang fallback.
    /// </summary>
    public int RedisTimeoutMs { get; set; } = 150;

    /// <summary>
    /// Timeout tối thiểu khi kết nối Redis (ms). Timeout kết nối thực tế là <c>max(RedisTimeoutMs, RedisConnectTimeoutMs)</c>,
    /// trừ khi host còn là placeholder của template cấu hình (khi đó dùng <c>RedisTimeoutMs</c> để không chờ vô ích). Mặc định 3000.
    /// </summary>
    public int RedisConnectTimeoutMs { get; set; } = 3000;
}
