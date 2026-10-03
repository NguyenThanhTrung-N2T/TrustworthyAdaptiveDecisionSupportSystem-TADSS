using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Persistence;
using TADSS.Infrastructure.Resilience;

namespace TADSS.Infrastructure;

/// <summary>
/// Đăng ký DI cho tầng Infrastructure: DbContext và kho audit (PostgreSQL), health check, pipeline chịu lỗi (Polly)
/// và cache quyết định (memory/Redis). Phần use case do <c>AddApplication</c> đăng ký.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký adapter hạ tầng của TADSS: <see cref="TadssDbContext"/>, <see cref="DatabaseOptions"/>, health check PostgreSQL,
    /// <see cref="IStrategyPipeline"/>, <see cref="IStrategyDecisionCache"/> và <see cref="IDecisionEvaluationStore"/> / <see cref="IDecisionQueryStore"/> (cùng một <see cref="EfDecisionAuditStore"/> trong mỗi scope).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 1. DbContext: thời gian audit do DecisionService gán, không có interceptor ghi timestamp.
        // Migration nằm trong assembly Infrastructure (cùng assembly với TadssDbContext).
        services.AddDbContext<TadssDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(TadssDbContext).Assembly.GetName().Name);
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "tadss");
            });
        });
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        // 2. Health check có phân nhóm tag (Liveness / Readiness)
        services.AddPersistenceHealthChecks(connectionString);

        // 3. Cấu hình chịu lỗi (section Tadss:Resilience): kiểm tra ngân sách thời gian khi khởi động.
        // Pipeline là singleton: circuit breaker dùng chung cho mọi request.
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
        services.AddSingleton<IStrategyPipeline, StrategyPipeline>();

        // 4. Cache StrategyDecision (không cache response), section Tadss:Cache; tự đủ phụ thuộc nên
        // đăng ký IMemoryCache/options ở đây. Multiplexer Redis cũng đăng ký tại đây
        // (TryAdd: đăng ký có sẵn thắng); nó chỉ được tạo khi Provider = Redis được resolve.
        services.AddMemoryCache();
        services.AddOptions<TadssCacheOptions>().Bind(configuration.GetSection(TadssCacheOptions.SectionName));
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
            RedisConnectionFactory.Create(sp.GetRequiredService<IOptions<TadssCacheOptions>>().Value));
        services.AddSingleton<IStrategyDecisionCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TadssCacheOptions>>();
            var logger = sp.GetRequiredService<ILogger<StrategyDecisionCache>>();
            var useRedis = string.Equals(options.Value.Provider, "Redis", StringComparison.OrdinalIgnoreCase);
            return new StrategyDecisionCache(
                sp.GetRequiredService<IMemoryCache>(),
                options,
                logger,
                useRedis ? RedisConnectionFactory.ResolveOrWarn(sp, logger) : null);
        });

        // 5. Kho audit scoped vì dùng TadssDbContext của scope (request). Đăng ký MỘT lần; hai port (ISP) forward tới cùng instance trong scope
        services.AddScoped<EfDecisionAuditStore>();
        services.AddScoped<IDecisionEvaluationStore>(sp => sp.GetRequiredService<EfDecisionAuditStore>());
        services.AddScoped<IDecisionQueryStore>(sp => sp.GetRequiredService<EfDecisionAuditStore>());

        return services;
    }
}
