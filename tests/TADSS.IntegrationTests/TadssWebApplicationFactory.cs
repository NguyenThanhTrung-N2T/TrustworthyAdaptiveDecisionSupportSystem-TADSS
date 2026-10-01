using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;

namespace TADSS.IntegrationTests;

/// <summary>
/// Host TADSS cho integration test, trỏ tới PostgreSQL và Redis thật (container Testcontainers).
/// Chạy ở môi trường <c>Testing</c> nên không nạp <c>appsettings.Development.json</c> hay User Secrets.
/// </summary>
public class TadssWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    private readonly string _connectionString;
    private readonly string _redisConnectionString;

    public TadssWebApplicationFactory(string connectionString, string redisConnectionString)
    {
        _connectionString = connectionString;
        _redisConnectionString = redisConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureServices(services =>
        {
            ReplaceDbContext(services, _connectionString);
            services.PostConfigure<TadssCacheOptions>(options =>
            {
                options.Provider = "Redis";
                options.RedisConnectionString = _redisConnectionString;
            });
        });
    }

    /// <summary>
    /// Thay đăng ký DbContext của app bằng Npgsql trỏ tới <paramref name="connectionString"/>,
    /// giữ <see cref="AuditTimestampInterceptor"/> và thêm interceptor riêng của test nếu có.
    /// </summary>
    public static void ReplaceDbContext(IServiceCollection services, string connectionString, params IInterceptor[] additionalInterceptors)
    {
        services.RemoveAll<DbContextOptions<TadssDbContext>>();
        services.AddDbContext<TadssDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"));
            options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
            options.AddInterceptors(additionalInterceptors);
        });
    }
}
