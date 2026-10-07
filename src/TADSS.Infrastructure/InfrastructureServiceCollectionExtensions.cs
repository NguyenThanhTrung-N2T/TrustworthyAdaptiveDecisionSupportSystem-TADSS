using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Configuration;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Common;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Configuration;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Persistence;
using TADSS.Infrastructure.Resilience;
using TADSS.Infrastructure.Shadow;
using TADSS.Infrastructure.Telemetry;

namespace TADSS.Infrastructure;

/// <summary>
/// Đăng ký DI cho tầng Infrastructure: DbContext và kho audit (PostgreSQL), health check, pipeline chịu lỗi (Polly),
/// cache quyết định (memory/Redis) và nguồn cấu hình định tuyến (đọc/theo dõi <see cref="IConfiguration"/>). Phần use case do <c>AddApplication</c> đăng ký.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký adapter hạ tầng của TADSS: <see cref="TadssDbContext"/>, <see cref="DatabaseOptions"/>, health check PostgreSQL,
    /// <see cref="IStrategyPipeline"/>, <see cref="IStrategyDecisionCache"/>, <see cref="IRoutingRulesSource"/> và <see cref="IDecisionEvaluationStore"/> / <see cref="IDecisionQueryStore"/> (cùng một <see cref="EfDecisionAuditStore"/> trong mỗi scope).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Cấu hình database: chuỗi kết nối đọc từ IConfiguration cuối cùng trong DI (gồm override của host test), không chụp lúc đăng ký.
        // Thiếu hoặc sai chuỗi thì host không khởi động ở mọi môi trường (DatabaseOptionsValidator).
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Configure<IConfiguration>((options, finalConfiguration) =>
                options.ConnectionString = finalConfiguration.GetConnectionString("DefaultConnection"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        // 2. DbContext (EF Core nằm trong namespace Persistence): thời gian audit do DecisionService gán, không có interceptor ghi timestamp.
        // Migration nằm trong assembly Infrastructure (cùng assembly với TadssDbContext).
        services.AddPersistenceDbContext();

        // 3. Health check có phân nhóm tag (Liveness / Readiness)
        services.AddPersistenceHealthChecks();

        // 4. Cấu hình chịu lỗi (section Tadss:Resilience): kiểm tra ngân sách thời gian khi khởi động.
        // Pipeline là singleton nhưng giữ một pipeline (và circuit breaker) riêng cho từng strategy, tạo lười lần đầu dùng.
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
        services.AddSingleton<IStrategyPipeline, StrategyPipeline>();

        // 5. Cache StrategyDecision (không cache response), section Tadss:Cache; tự đủ phụ thuộc nên
        // đăng ký IMemoryCache/options ở đây. Multiplexer Redis cũng đăng ký tại đây
        // (TryAdd: đăng ký có sẵn thắng); nó chỉ được tạo khi Provider = Redis được resolve.
        services.AddMemoryCache();
        services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CacheOptions>, CacheOptionsValidator>();
        // Health check Redis (chỉ khi Provider = Redis): lỗi Redis chỉ làm readiness Degraded
        services.AddCacheHealthChecks(configuration);
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            RedisConnectionFactory.Create(sp.GetRequiredService<IOptions<CacheOptions>>().Value));
        services.AddSingleton<IStrategyDecisionCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<CacheOptions>>();
            var logger = sp.GetRequiredService<ILogger<StrategyDecisionCache>>();
            var useRedis = CacheProviders.IsRedis(options.Value.Provider);
            return new StrategyDecisionCache(
                sp.GetRequiredService<IMemoryCache>(),
                options,
                sp.GetRequiredService<TadssMetrics>(),
                sp.GetRequiredService<TimeProvider>(),
                logger,
                useRedis ? RedisConnectionFactory.ResolveOrWarn(sp, logger) : null);
        });

        // 5b. Cấu hình tiêm lỗi strategy (section Tadss:Testing:FaultInjection): luôn đăng ký và kiểm tra khi khởi động ở mọi môi trường;
        // việc có bọc decorator hay không do composition root quyết định theo môi trường (chỉ Testing và Enabled).
        services.AddOptions<FaultInjectionOptions>()
            .Bind(configuration.GetSection(FaultInjectionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FaultInjectionOptions>, FaultInjectionOptionsValidator>();

        // 5c. Nguồn cấu hình định tuyến Tadss:Routing cho StrategyRouter: đọc và theo dõi IConfiguration cuối cùng trong DI (gồm override của host test),
        // không chụp lúc đăng ký. Singleton, không giữ trạng thái.
        services.AddSingleton<IRoutingRulesSource, ConfigurationRoutingRulesSource>();

        // 5d. Worker nền chạy việc shadow (Tadss:Shadow): kết thúc ngay khi shadow tắt
        services.AddHostedService<ShadowExecutionWorker>();

        // 5e. Telemetry (section Tadss:Telemetry): options luôn đăng ký và kiểm tra khi khởi động ở mọi môi trường; worker làm mới gauge hàng chờ duyệt
        // kết thúc ngay khi telemetry tắt. Bộ máy OpenTelemetry (exporter) do composition root dựng, không nằm ở tầng này.
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();
        services.AddHostedService<PendingApprovalsMetricRefresher>();

        // 6. Kho audit scoped vì dùng TadssDbContext của scope (request). Đăng ký MỘT lần; hai port (ISP) forward tới cùng instance trong scope
        services.AddScoped<EfDecisionAuditStore>();
        services.AddScoped<IDecisionEvaluationStore>(sp => sp.GetRequiredService<EfDecisionAuditStore>());
        services.AddScoped<IDecisionQueryStore>(sp => sp.GetRequiredService<EfDecisionAuditStore>());
        services.AddScoped<IDecisionGovernanceStore, EfDecisionGovernanceStore>();

        return services;
    }
}
