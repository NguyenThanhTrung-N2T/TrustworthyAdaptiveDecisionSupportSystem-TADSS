using Microsoft.EntityFrameworkCore;
using Shared.DecisionContractSDK.Interfaces;
using StackExchange.Redis;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Services;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;

namespace TADSS.Runtime.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Interceptor quản lý Timestamp
        services.AddSingleton<AuditTimestampInterceptor>();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 2. DbContext với DI Interceptor
        services.AddDbContext<TadssDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "tadss");
            });

            options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
        });

        // 3. Health Checks có phân nhóm Tags (Liveness / Readiness)
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: connectionString ?? string.Empty,
                name: "Neon-PostgreSQL",
                tags: new[] { "ready", "db" });

        // 4. Decision Strategies & Strategy Resolver (Singleton, Stateless)
        services.Configure<StrategyOptions>(configuration.GetSection(StrategyOptions.SectionName));
        services.AddSingleton<IReservationDecisionStrategy, DefaultDecisionStrategy>();
        services.AddSingleton<IReservationDecisionStrategy, MockAlternativeDecisionStrategy>();
        services.AddSingleton<IStrategyResolver, StrategyResolver>();

        // 5. Resilience Pipeline & Fallback Strategy
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));
        services.AddSingleton<FallbackDecisionStrategy>();
        services.AddSingleton<IDecisionResiliencePipeline, DecisionResiliencePipeline>();

        // 5. Distributed & In-Memory Caching Services (Task 4.3)
        services.AddMemoryCache();
        services.Configure<TadssCacheOptions>(configuration.GetSection(TadssCacheOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cacheOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TadssCacheOptions>>().Value;
            var redisConfig = StackExchange.Redis.ConfigurationOptions.Parse(cacheOptions.RedisConnectionString);
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ConnectTimeout = cacheOptions.RedisTimeoutMs;
            redisConfig.SyncTimeout = cacheOptions.RedisTimeoutMs;
            return StackExchange.Redis.ConnectionMultiplexer.Connect(redisConfig);
        });

        services.AddSingleton<MemoryDecisionCacheService>();
        services.AddSingleton<RedisDecisionCacheService>();
        services.AddSingleton<IDecisionCacheService, ResilientDecisionCacheService>();

        // 6. Decision Application Services
        services.AddScoped<IDecisionService, DecisionService>();

        return services;
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "TADSS Decision Runtime API",
                Version = "v1",
                Description = "Decision Runtime Engine for SmashCourt reservation requests"
            });

            // Load API XML documentation if present
            var apiXmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var apiXmlPath = Path.Combine(AppContext.BaseDirectory, apiXmlFile);
            if (File.Exists(apiXmlPath))
            {
                c.IncludeXmlComments(apiXmlPath);
            }

            // Load Shared SDK XML documentation if present
            var sdkXmlFile = "Shared.DecisionContractSDK.xml";
            var sdkXmlPath = Path.Combine(AppContext.BaseDirectory, sdkXmlFile);
            if (File.Exists(sdkXmlPath))
            {
                c.IncludeXmlComments(sdkXmlPath);
            }

            // Register operation filter for contract-defined headers
            c.OperationFilter<RequiredHeaderOperationFilter>();
        });

        return services;
    }
}
