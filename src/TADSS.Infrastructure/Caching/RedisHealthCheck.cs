using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Health check Redis cho <c>/health/ready</c>: gửi <c>PING</c> tới multiplexer dùng chung. Redis chỉ là cache có fallback memory nên mọi lỗi
/// (không có/không tạo được multiplexer, <c>PING</c> lỗi hoặc quá <see cref="CacheOptions.RedisConnectTimeoutMs"/>) cho kết quả <see cref="HealthStatus.Degraded"/>,
/// không bao giờ làm readiness <c>Unhealthy</c>. Description không chứa chuỗi kết nối hay message của exception; log chỉ ghi tên loại exception.
/// </summary>
internal sealed class RedisHealthCheck(
    IServiceProvider services,
    IOptions<CacheOptions> options,
    ILogger<RedisHealthCheck> logger) : IHealthCheck
{
    /// <summary>Tên health check Redis.</summary>
    public const string Name = "redis";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IConnectionMultiplexer? multiplexer;
        try
        {
            multiplexer = services.GetService<IConnectionMultiplexer>();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Health check Redis: không tạo được kết nối ({ExceptionType}).", ex.GetType().Name);
            return HealthCheckResult.Degraded("Redis không khả dụng; cache dùng memory.");
        }

        if (multiplexer is null)
        {
            logger.LogWarning("Health check Redis: không có IConnectionMultiplexer.");
            return HealthCheckResult.Degraded("Redis không khả dụng; cache dùng memory.");
        }

        try
        {
            var timeout = TimeSpan.FromMilliseconds(options.Value.RedisConnectTimeoutMs);
            await multiplexer.GetDatabase().PingAsync().WaitAsync(timeout, cancellationToken);
            return HealthCheckResult.Healthy("Redis phản hồi PING.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Health check Redis: PING thất bại ({ExceptionType}).", ex.GetType().Name);
            return HealthCheckResult.Degraded("Redis không khả dụng; cache dùng memory.");
        }
    }
}
