namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Cấu hình các tham số chịu lỗi (Resilience Pipeline Options) cho TADSS Decision Runtime Engine.
/// </summary>
public class ResilienceOptions
{
    public const string SectionName = "Tadss:Resilience";

    /// <summary>
    /// Hard deadline toàn cục cho toàn bộ chu trình ra quyết định (mặc định 500ms).
    /// </summary>
    public int OuterTimeoutMs { get; set; } = 500;

    /// <summary>
    /// Timeout tối đa cho 1 lượt gọi Strategy đơn lẻ (mặc định 150ms).
    /// </summary>
    public int InnerTimeoutMs { get; set; } = 150;

    /// <summary>
    /// Số lần retry tối đa khi gặp lỗi nhất thời (mặc định 2 lần -> tối đa 3 attempts).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    /// Khoảng thời gian chờ cơ bản giữa các lần retry (mặc định 30ms).
    /// </summary>
    public int RetryBaseDelayMs { get; set; } = 30;

    /// <summary>
    /// Khoảng thời gian lấy mẫu thống kê tỷ lệ lỗi của Circuit Breaker (mặc định 30s).
    /// </summary>
    public int CircuitBreakerSamplingSeconds { get; set; } = 30;

    /// <summary>
    /// Tỷ lệ lỗi tối thiểu để kích hoạt mở mạch Circuit Breaker (mặc định 0.5 = 50%).
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Số lượng request tối thiểu trong khung lấy mẫu trước khi đánh giá tỷ lệ lỗi (mặc định 10).
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Thời gian giữ mạch hở (Open state) trước khi thử lại Half-Open (mặc định 15s).
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 15;
}
