using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Persistence;

namespace TADSS.IntegrationTests;

/// <summary>
/// Host TADSS cho integration test, trỏ tới PostgreSQL và Redis thật (container Testcontainers).
/// Chạy ở môi trường <c>Testing</c> nên không nạp <c>appsettings.Development.json</c> hay User Secrets.
/// </summary>
public class TadssWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    /// <summary>Timeout thao tác Redis của host thử nghiệm (ms); rộng hơn mặc định để tránh miss giả khi máy tải nặng.</summary>
    public const int RedisTimeoutMs = 2000;

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
        // appsettings.json chỉ có chuỗi giữ chỗ (DatabaseOptionsValidator từ chối): cấp chuỗi của container qua cấu hình, như người vận hành cấp qua biến môi trường
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", _connectionString)]));
        builder.ConfigureServices(services =>
        {
            ReplaceDbContext(services, _connectionString);
            services.PostConfigure<CacheOptions>(options =>
            {
                options.Provider = "Redis";
                options.RedisConnectionString = _redisConnectionString;
                // CACHE_HIT chỉ được đến từ Redis thật: không có đường lùi sang memory cache
                options.EnableFallbackToMemory = false;
                // 150 ms mặc định dễ làm một lần đọc Redis thành "miss" khi máy chạy test đang tải nặng, khiến test cache chập chờn
                options.RedisTimeoutMs = RedisTimeoutMs;
            });
        });
    }

    /// <summary>
    /// Thay đăng ký DbContext của app bằng Npgsql trỏ tới <paramref name="connectionString"/>,
    /// và thêm interceptor riêng của test nếu có.
    /// </summary>
    public static void ReplaceDbContext(IServiceCollection services, string connectionString, params IInterceptor[] additionalInterceptors)
    {
        services.RemoveAll<DbContextOptions<TadssDbContext>>();
        services.AddDbContext<TadssDbContext>(options =>
        {
            options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tadss"));
            options.AddInterceptors(additionalInterceptors);
        });
    }
}
