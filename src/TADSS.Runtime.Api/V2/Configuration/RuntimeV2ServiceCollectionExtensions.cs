using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.V2.Domain;
using StackExchange.Redis;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Infrastructure.Caching;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Services.Decisions;
using TADSS.Runtime.Api.V2.Services.Strategies;

namespace TADSS.Runtime.Api.V2.Configuration;

/// <summary>
/// Nơi duy nhất đăng ký DI cho runtime v2. Mỗi nhóm thành phần của gói 5.2b bổ sung đăng ký của mình tại đây.
/// </summary>
public static class RuntimeV2ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký cấu hình (có kiểm tra khi khởi động), lịch cao điểm và các dịch vụ nền của runtime v2.
    /// </summary>
    public static IServiceCollection AddDecisionRuntimeV2(this IServiceCollection services, IConfiguration configuration)
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

        // Strategy đang kích hoạt dùng chung section Tadss:Strategy với v1 (bind lại cùng section là vô hại)
        services.AddOptions<StrategyOptions>().Bind(configuration.GetSection(StrategyOptions.SectionName));

        // Strategy v2 dựng từ Tadss:Strategies:* + lịch cao điểm; lỗi cấu hình làm registry không dựng được
        // (Program.cs resolve sớm nên ứng dụng không khởi động)
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

        // Cấu hình chịu lỗi dùng chung section Tadss:Resilience với v1: kiểm tra ngân sách thời gian khi khởi động
        // (áp dụng cho cả v1). Pipeline v2 là singleton riêng, không chia sẻ circuit breaker với v1.
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
        services.AddSingleton<IStrategyPipeline, StrategyPipeline>();

        // Cache StrategyDecision (không cache response) dùng chung section Tadss:Cache với v1; tự đủ phụ thuộc nên
        // đăng ký IMemoryCache/options ở đây (đăng ký lại cùng loại với v1 là vô hại). Multiplexer Redis cũng đăng ký tại đây
        // (TryAdd: đăng ký có sẵn thắng) để cache v2 không phụ thuộc DI của v1; nó chỉ được tạo khi Provider = Redis được resolve.
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
    /// Tạo multiplexer Redis theo cùng quy tắc của cache v1: parse chuỗi kết nối, <c>AbortOnConnectFail = false</c> (host
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
