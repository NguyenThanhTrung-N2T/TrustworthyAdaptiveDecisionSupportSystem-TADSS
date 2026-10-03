using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Dựng và lấy multiplexer Redis cho <see cref="StrategyDecisionCache"/>.
/// </summary>
internal static class RedisConnectionFactory
{
    /// <summary>
    /// Tạo multiplexer Redis: parse chuỗi kết nối, <c>AbortOnConnectFail = false</c> (host
    /// không với tới thì multiplexer vẫn dựng được ở trạng thái ngắt kết nối), timeout thao tác theo <c>RedisTimeoutMs</c>,
    /// timeout kết nối tối thiểu 3000 ms trừ khi host còn là placeholder của template cấu hình.
    /// </summary>
    public static IConnectionMultiplexer Create(TadssCacheOptions cacheOptions)
    {
        var redisConfig = ConfigurationOptions.Parse(cacheOptions.RedisConnectionString);
        redisConfig.AbortOnConnectFail = false;
        var isPlaceholderHost = cacheOptions.RedisConnectionString.Contains("your-redis-host", StringComparison.OrdinalIgnoreCase);
        redisConfig.ConnectTimeout = isPlaceholderHost
            ? cacheOptions.RedisTimeoutMs
            : Math.Max(cacheOptions.RedisTimeoutMs, 3000);
        redisConfig.SyncTimeout = cacheOptions.RedisTimeoutMs;
        redisConfig.AsyncTimeout = cacheOptions.RedisTimeoutMs;
        return ConnectionMultiplexer.Connect(redisConfig);
    }

    /// <summary>
    /// Lấy multiplexer khi <c>Provider = Redis</c>; không lấy được thì ghi một Warning rồi để cache chỉ dùng memory (không ném).
    /// Không ghi message của exception vì chuỗi kết nối có thể chứa mật khẩu.
    /// </summary>
    public static IConnectionMultiplexer? ResolveOrWarn(IServiceProvider services, ILogger<StrategyDecisionCache> logger)
    {
        try
        {
            var multiplexer = services.GetService<IConnectionMultiplexer>();
            if (multiplexer is null)
            {
                logger.LogWarning("Provider = Redis nhưng không có IConnectionMultiplexer; cache quyết định v2 chỉ dùng memory.");
            }

            return multiplexer;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Provider = Redis nhưng không tạo được kết nối Redis ({ExceptionType}); cache quyết định v2 chỉ dùng memory.",
                ex.GetType().Name);
            return null;
        }
    }
}
