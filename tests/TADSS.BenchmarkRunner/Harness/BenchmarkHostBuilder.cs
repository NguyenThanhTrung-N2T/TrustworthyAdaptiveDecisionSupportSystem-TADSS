using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Interceptors;
using TADSS.Runtime.Api.Services;

[assembly: WebApplicationFactoryContentRoot(
    "TADSS.Runtime.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "src/TADSS.Runtime.Api",
    "Program.cs",
    "1")]
[assembly: WebApplicationFactoryContentRoot(
    "TADSS.Runtime.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "../../../../../src/TADSS.Runtime.Api",
    "Program.cs",
    "2")]

namespace TADSS.BenchmarkRunner.Harness;

/// <summary>
/// Builder chuyên trách khởi tạo WebApplicationFactory cô lập cho mỗi lượt chạy thực nghiệm,
/// gom toàn bộ logic thay thế DI ServiceDescriptor về các hàm helper tái sử dụng.
/// </summary>
public sealed class BenchmarkHostBuilder
{
    private readonly ScenarioConfiguration _scenarioConfig;
    private readonly string _databaseName;

    public BenchmarkHostBuilder(ScenarioConfiguration scenarioConfig, string databaseName)
    {
        _scenarioConfig = scenarioConfig;
        _databaseName = databaseName;
    }

    public WebApplicationFactory<global::Program> Build()
    {
        EnsureMvcTestingManifest();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Fatal()
            .CreateLogger();

        return new BenchmarkWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            ConfigureHostSettings(builder, _scenarioConfig);
            ConfigureSilentLogging(builder);
            builder.ConfigureServices(services => ConfigureIsolatedServices(services, _scenarioConfig, _databaseName));
        });
    }

    private static void ConfigureHostSettings(IWebHostBuilder builder, ScenarioConfiguration config)
    {
        builder.UseContentRoot(ResolveApiContentRoot());
        builder.UseSetting("Serilog:MinimumLevel:Default", "Fatal");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft", "Fatal");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.AspNetCore", "Fatal");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore", "Fatal");
        builder.UseSetting("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command", "Fatal");

        if (!config.UseLiveRedisCloud)
        {
            builder.UseSetting("Tadss:Cache:Provider", "Memory");
        }
    }

    private static void ConfigureSilentLogging(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.None);
        });
    }

    private static void ConfigureIsolatedServices(
        IServiceCollection services,
        ScenarioConfiguration config,
        string databaseName)
    {
        // 1. Cô lập Database theo từng lượt chạy + gắn Interceptor giả lập DB Contention (nếu có)
        RemoveServiceDescriptor<DbContextOptions<TadssDbContext>>(services);
        services.AddDbContext<TadssDbContext>((sp, options) =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.AddInterceptors(sp.GetRequiredService<AuditTimestampInterceptor>());
            if (config.DbContentionInterceptor != null)
            {
                options.AddInterceptors(config.DbContentionInterceptor);
            }
        });

        // 2. Đăng ký Strategy thực nghiệm có bộ đếm nguyên tử
        ReplaceSingleton<IStrategyResolver>(services, new SingleStrategyResolver(config.Strategy));

        // 3. Khóa trạng thái Cache theo đúng kịch bản (NoOp / SerializedInProcess / NetworkLatencyWAN / BoundedLRU)
        if (!config.UseLiveRedisCloud)
        {
            ReplaceSingleton<IDecisionCacheService>(services, config.CustomCacheService);
        }

        // 4. Khóa cấu hình Polly Resilience theo ScenarioConfiguration
        services.Configure(config.ConfigureResilience);
    }

    public static void RemoveServiceDescriptor<TService>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }

    public static void ReplaceSingleton<TService>(IServiceCollection services, TService implementationInstance)
        where TService : class
    {
        RemoveServiceDescriptor<TService>(services);
        services.AddSingleton(implementationInstance);
    }

    public static void ReplaceSingleton<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        RemoveServiceDescriptor<TService>(services);
        services.AddSingleton<TService, TImplementation>();
    }

    private static void EnsureMvcTestingManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "MvcTestingAppManifest.json");
        if (!File.Exists(manifestPath))
        {
            var apiRoot = ResolveApiContentRoot();
            var manifestJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["TADSS.Runtime.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"] = apiRoot
            });
            File.WriteAllText(manifestPath, manifestJson);
        }
    }

    private static string ResolveApiContentRoot()
    {
        var fromCwd = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "TADSS.Runtime.Api"));
        if (Directory.Exists(fromCwd))
        {
            return fromCwd;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TADSS.Runtime.Api"));
    }

    private sealed class BenchmarkWebApplicationFactory : WebApplicationFactory<global::Program>
    {
    }
}
