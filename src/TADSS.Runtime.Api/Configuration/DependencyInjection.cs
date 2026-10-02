using Microsoft.EntityFrameworkCore;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Configuration;

namespace TADSS.Runtime.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 1. DbContext: thời gian audit do DecisionService gán, không có interceptor ghi timestamp
        services.AddDbContext<TadssDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "tadss");
            });
        });
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        // 2. Health Checks có phân nhóm Tags (Liveness / Readiness)
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: connectionString ?? string.Empty,
                name: "Neon-PostgreSQL",
                tags: new[] { "ready", "db" });

        // 3. Lõi runtime: strategy registry, resilience, cache, audit store — nơi đăng ký duy nhất cho lõi
        services.AddDecisionRuntime(configuration);

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
