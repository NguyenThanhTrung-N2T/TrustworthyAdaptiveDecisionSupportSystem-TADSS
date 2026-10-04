using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Đăng ký health check Redis cho cache quyết định (nhóm <c>ready</c>, dùng cho endpoint <c>/health/ready</c>).
/// </summary>
internal static class CacheHealthChecks
{
    /// <summary>
    /// Khi <c>Tadss:Cache:Provider = Redis</c>, thêm check <see cref="RedisHealthCheck.Name"/> với <c>failureStatus = Degraded</c>
    /// (Redis hỏng làm readiness <c>Degraded</c>, vẫn 200). Provider <c>Memory</c> thì không đăng ký gì.
    /// </summary>
    public static IServiceCollection AddCacheHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()?.Provider ?? CacheProviders.Redis;
        if (CacheProviders.IsRedis(provider))
        {
            services.AddHealthChecks()
                .AddCheck<RedisHealthCheck>(
                    RedisHealthCheck.Name,
                    failureStatus: HealthStatus.Degraded,
                    tags: new[] { HealthCheckTags.Ready });
        }

        return services;
    }
}
