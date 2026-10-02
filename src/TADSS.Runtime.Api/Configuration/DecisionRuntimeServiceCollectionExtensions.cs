using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using StackExchange.Redis;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Services.Decisions;
using TADSS.Runtime.Api.Services.Strategies;

namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Nơi duy nhất đăng ký DI cho lõi runtime (contract v2). Mỗi nhóm thành phần bổ sung đăng ký của mình tại đây.
/// </summary>
public static class DecisionRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký cấu hình (có kiểm tra khi khởi động), lịch cao điểm và các dịch vụ nền của runtime.
    /// </summary>
    public static IServiceCollection AddDecisionRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        // Thời gian lấy qua TimeProvider để test điều khiển được đồng hồ
        services.TryAddSingleton(TimeProvider.System);

        // Options + validator fail-fast: cấu hình sai thì ứng dụng không khởi động
        services.AddOptions<ApprovalOptions>()
            .Bind(configuration.GetSection(ApprovalOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApprovalOptions>, ApprovalOptionsValidator>();

        services.AddOptions<ValidationOptions>()
            .Bind(configuration.GetSection(ValidationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ValidationOptions>, ValidationOptionsValidator>();

        services.AddOptions<CalendarOptions>()
            .Bind(configuration.GetSection(CalendarOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CalendarOptions>, CalendarOptionsValidator>();

        // Lịch cao điểm dùng chung cho router và mọi strategy (nguồn duy nhất của "giờ cao điểm")
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<CalendarOptions>>().Value.ToCalendar());

        services.AddSingleton<ItemDurationPolicy>();
        services.AddSingleton<DepositCalculator>();
        services.AddSingleton<ApprovalWindowPolicy>();

        // Strategy đang kích hoạt: section Tadss:Strategy
        services.AddOptions<StrategyOptions>().Bind(configuration.GetSection(StrategyOptions.SectionName));

        // Strategy v2 dựng từ Tadss:Strategies:* + lịch cao điểm; lỗi cấu hình làm registry không dựng được
        // (Program.cs resolve sớm nên ứng dụng không khởi động)
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

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

        // Bộ ghép response + bản ghi audit. IDecisionAuditStore chưa đăng ký ở 5.2b (B-09): EfDecisionAuditStore thuộc 5.2c
        services.AddSingleton<DecisionResponseComposer>();

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
