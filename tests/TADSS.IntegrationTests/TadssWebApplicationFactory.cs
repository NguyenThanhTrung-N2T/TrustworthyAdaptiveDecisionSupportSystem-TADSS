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
using TADSS.UnitTests.TestData;

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

    /// <summary>Môi trường của host (mặc định <see cref="EnvironmentName"/>); đặt <c>Staging</c> để kiểm tra các quy tắc fail-closed ngoài Development/Testing.</summary>
    public string HostEnvironment { get; init; } = EnvironmentName;

    /// <summary>
    /// API key được cấu hình cho host (mặc định chỉ <see cref="TestApiKeys.Default"/>); danh sách rỗng nghĩa là <c>ApiKeys</c> rỗng.
    /// Mọi <see cref="HttpClient"/> mặc định gửi <see cref="TestApiKeys.DefaultKey"/> (xem <see cref="ConfigureClient"/>).
    /// </summary>
    internal IReadOnlyList<TestApiKeys.Client> ApiKeys { get; init; } = TestApiKeys.DefaultClients;

    /// <summary>
    /// Bật làm nóng lúc khởi động (<c>Tadss:Warmup</c>). Mặc định tắt: làm nóng chạy mọi strategy và truy vấn store, nên test đếm số lần gọi strategy
    /// hay số truy vấn sẽ không còn tất định; chỉ test của chính bước làm nóng bật nó.
    /// </summary>
    public bool Warmup { get; init; }

    /// <summary>Mọi client (kể cả của factory dẫn xuất từ <c>WithWebHostBuilder</c>) mặc định gửi API key mặc định nên test cũ không phải sửa từng chỗ.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        TestApiKeys.ApplyDefaultKey(client);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        // appsettings.json chỉ có chuỗi giữ chỗ (DatabaseOptionsValidator từ chối) và ApiKeys rỗng: cấp chuỗi của container và API key giả
        // qua cấu hình, như người vận hành cấp qua biến môi trường
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:DefaultConnection", _connectionString)]);
            configuration.AddInMemoryCollection(TestApiKeys.ToConfiguration(ApiKeys));
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Tadss:Warmup:Enabled", Warmup ? "true" : "false")]);
        });
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
