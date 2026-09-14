namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Cấu hình cho tầng Caching phân tán (Redis + In-Memory Fallback) của TADSS.
/// </summary>
public class TadssCacheOptions
{
    public const string SectionName = "Tadss:Cache";

    /// <summary>
    /// Nhà cung cấp cache chính: "Redis" hoặc "Memory". Mặc định "Redis".
    /// </summary>
    public string Provider { get; set; } = "Redis";

    /// <summary>
    /// Connection string cho Redis instance (ví dụ "localhost:6379").
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Tiền tố namespace trong Redis (ví dụ "tadss:").
    /// </summary>
    public string InstanceName { get; set; } = "tadss:";

    /// <summary>
    /// Thời gian sống mặc định của cache (tính bằng giờ). Mặc định 24h.
    /// </summary>
    public int TtlHours { get; set; } = 24;

    /// <summary>
    /// Số lượng bản ghi tối đa cho In-Memory Cache (áp dụng LRU eviction).
    /// </summary>
    public int MaxMemoryCacheSize { get; set; } = 10000;

    /// <summary>
    /// Cho phép tự động fallback sang In-Memory Cache khi Redis mất kết nối hoặc timeout.
    /// </summary>
    public bool EnableFallbackToMemory { get; set; } = true;

    /// <summary>
    /// Timeout tối đa cho thao tác Redis (ms) trước khi chuyển sang fallback.
    /// </summary>
    public int RedisTimeoutMs { get; set; } = 150;
}
