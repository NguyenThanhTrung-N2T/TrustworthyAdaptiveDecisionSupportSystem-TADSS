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
    /// Tiền tố namespace cộng vào mọi khóa cache (Redis và memory), mặc định "tadss:" nên khóa đầy đủ là <c>tadss:v2:...</c>.
    /// Không rỗng, tối đa 32 ký tự, không chứa khoảng trắng.
    /// </summary>
    public string InstanceName { get; set; } = "tadss:";

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

    /// <summary>
    /// Tỷ lệ lệnh Redis lỗi (exception hoặc quá hạn) trong khung lấy mẫu để circuit breaker Redis mở, trong (0, 1]. Mặc định 0.5.
    /// </summary>
    public double RedisBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Độ dài khung lấy mẫu của circuit breaker Redis (giây), tối thiểu 1. Mặc định 30.
    /// </summary>
    public int RedisBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>
    /// Số lệnh Redis tối thiểu trong khung lấy mẫu trước khi breaker được phép mở, tối thiểu 2. Mặc định 10.
    /// </summary>
    public int RedisBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Thời gian breaker Redis giữ trạng thái mở (giây) trước khi thăm dò lại, tối thiểu 1. Mặc định 30.
    /// </summary>
    public int RedisBreakerBreakDurationSeconds { get; set; } = 30;
}
