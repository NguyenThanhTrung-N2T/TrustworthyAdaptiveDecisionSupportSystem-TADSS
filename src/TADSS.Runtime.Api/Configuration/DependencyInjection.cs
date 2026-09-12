using Microsoft.EntityFrameworkCore;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;
using TADSS.Runtime.Api.Middlewares;

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

        return services;
    }

    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        return services;
    }
}
