using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using StackExchange.Redis;
using TADSS.Application;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Services.Strategies;

namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Nơi duy nhất đăng ký DI cho lõi runtime (contract v2). Mỗi nhóm thành phần bổ sung đăng ký của mình tại đây.
/// </summary>
public static class DecisionRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký phần Application (qua <c>AddApplication</c>) cùng strategy, resilience, cache và kho audit của runtime.
    /// </summary>
    public static IServiceCollection AddDecisionRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        // Phần use case (options, policy, composer, registry, service) do tầng Application đăng ký
        services.AddApplication(configuration);

        // Strategy v2 dựng từ Tadss:Strategies:* + lịch cao điểm; lỗi cấu hình làm registry không dựng được
        // (Program.cs resolve sớm nên ứng dụng không khởi động)
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));

        // Cấu hình chịu lỗi (section Tadss:Resilience): kiểm tra ngân sách thời gian khi khởi động.
        // Pipeline là singleton: circuit breaker dùng chung cho mọi request.
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
        services.AddSingleton<IStrategyPipeline, StrategyPipeline>();

        // Cache StrategyDecision (không cache response), section Tadss:Cache; tự đủ phụ thuộc nên
        // đăng ký IMemoryCache/options ở đây. Multiplexer Redis cũng đăng ký tại đây
        // (TryAdd: đăng ký có sẵn thắng); nó chỉ được tạo khi Provider = Redis được resolve.
        services.AddMemoryCache();
        services.AddOptions<TadssCacheOptions>().Bind(configuration.GetSection(TadssCacheOptions.SectionName));
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            CreateRedisMultiplexer(sp.GetRequiredService<IOptions<TadssCacheOptions>>().Value));
        services.AddSingleton<IStrategyDecisionCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TadssCacheOptions>>();
            var logger = sp.GetRequiredService<ILogger<StrategyDecisionCache>>();
            var useRedis = string.Equals(options.Value.Provider, "Redis", StringComparison.OrdinalIgnoreCase);
            return new StrategyDecisionCache(
                sp.GetRequiredService<IMemoryCache>(),
                options,
                logger,
                useRedis ? ResolveRedisOrWarn(sp, logger) : null);
        });

        // Kho audit scoped vì dùng TadssDbContext của scope (request)
        services.AddScoped<IDecisionAuditStore, EfDecisionAuditStore>();

        return services;
    }

    /// <summary>
    /// Tạo multiplexer Redis: parse chuỗi kết nối, <c>AbortOnConnectFail = false</c> (host
    /// không với tới thì multiplexer vẫn dựng được ở trạng thái ngắt kết nối), timeout thao tác theo <c>RedisTimeoutMs</c>,
    /// timeout kết nối tối thiểu 3000 ms trừ khi host còn là placeholder của template cấu hình.
    /// </summary>
    private static IConnectionMultiplexer CreateRedisMultiplexer(TadssCacheOptions cacheOptions)
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
    private static IConnectionMultiplexer? ResolveRedisOrWarn(IServiceProvider services, ILogger<StrategyDecisionCache> logger)
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
