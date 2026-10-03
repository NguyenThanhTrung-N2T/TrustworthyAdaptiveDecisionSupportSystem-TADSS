using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.DecisionContractSDK.Decisions;
using StackExchange.Redis;
using TADSS.Application.Abstractions.Caching;
using TADSS.UnitTests.TestDoubles;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Caching;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: nối DI của cache trong <c>AddRuntimeCore</c> — <see cref="IStrategyDecisionCache"/> là singleton,
/// tự đăng ký đủ phụ thuộc (<c>IMemoryCache</c>, <c>CacheOptions</c>), dùng multiplexer Redis nếu có và
/// chỉ khi <c>Provider = Redis</c> (không kết nối Redis khi dùng provider Memory).
/// </summary>
public class StrategyDecisionCacheRegistrationTests
{
    private const string Key = "tadss:v2:Default:2.0.0:a1b2c3d4e5f60718:9f86d081884c7d659a2feaa0c55ad015";

    private static HostApplicationBuilder CreateBuilder(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair));
        builder.Services.AddLogging();
        return builder;
    }

    private static ServiceCollection CreateHostlessServices(
        CapturingLogger<StrategyDecisionCache> logger,
        params (string Key, string Value)[] values) =>
        CreateHostlessServices(logger, values, null);

    private static ServiceCollection CreateHostlessServices(
        CapturingLogger<StrategyDecisionCache> logger,
        (string Key, string Value)[] values,
        IConnectionMultiplexer? existingMultiplexer)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<StrategyDecisionCache>>(logger);
        if (existingMultiplexer is not null)
        {
            services.AddSingleton(existingMultiplexer);
        }

        services.AddRuntimeCore(configuration);
        return services;
    }

    private static ServiceProvider CreateHostlessProvider(
        CapturingLogger<StrategyDecisionCache> logger,
        params (string Key, string Value)[] values) =>
        CreateHostlessServices(logger, values).BuildServiceProvider();

    private static Mock<IConnectionMultiplexer> CreateMultiplexer(Mock<IDatabase> database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(database.Object);
        return multiplexer;
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddRuntimeCore_WithoutV1Registrations_ResolvesSingletonCache()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();

        var first = host.Services.GetRequiredService<IStrategyDecisionCache>();
        var second = host.Services.GetRequiredService<IStrategyDecisionCache>();

        first.Should().BeSameAs(second);
        var decision = StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]);
        await first.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);
        (await first.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddRuntimeCore_RedisProviderWithRegisteredMultiplexer_WritesToRedis()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var multiplexer = CreateMultiplexer(database);
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Redis"));
        builder.Services.AddSingleton(multiplexer.Object);
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();
        var cache = host.Services.GetRequiredService<IStrategyDecisionCache>();

        await cache.SetAsync(Key, StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]), TimeSpan.FromMinutes(60), CancellationToken.None);

        database.Verify(
            db => db.StringSetAsync((RedisKey)Key, It.IsAny<RedisValue>(), TimeSpan.FromMinutes(60), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void AddRuntimeCore_MemoryProvider_DoesNotResolveMultiplexer()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            _ => throw new InvalidOperationException("Provider Memory không được kết nối Redis"));
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();

        var act = () => host.Services.GetRequiredService<IStrategyDecisionCache>();

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddRuntimeCore_RedisProviderWithUnusableConnectionString_FallsBackToMemoryWithSingleWarning()
    {
        var logger = new CapturingLogger<StrategyDecisionCache>();
        var provider = CreateHostlessProvider(
            logger,
            ("Tadss:Cache:Provider", "Redis"),
            ("Tadss:Cache:RedisConnectionString", "localhost:6379,unknownOption=1"));

        var cache = provider.GetRequiredService<IStrategyDecisionCache>();
        var decision = StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]);
        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
        var warning = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        warning.Message.Should().NotContain("unknownOption", "chuỗi kết nối có thể chứa mật khẩu nên không được ghi log");
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void AddRuntimeCore_RedisProvider_RegistersMultiplexerWithoutConnecting()
    {
        var logger = new CapturingLogger<StrategyDecisionCache>();
        var services = CreateHostlessServices(
            logger,
            ("Tadss:Cache:Provider", "Redis"),
            ("Tadss:Cache:RedisConnectionString", "your-redis-host:11082"),
            ("Tadss:Cache:RedisTimeoutMs", "100"));

        var descriptor = services.Should().ContainSingle(item => item.ServiceType == typeof(IConnectionMultiplexer)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
        descriptor.ImplementationFactory.Should().NotBeNull("multiplexer phải được tạo lười, không kết nối khi đăng ký");

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IStrategyDecisionCache>();

        act.Should().NotThrow();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        multiplexer.IsConnected.Should().BeFalse("host chưa tồn tại và AbortOnConnectFail = false");
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void AddRuntimeCore_MemoryProvider_NeverCreatesOwnMultiplexer()
    {
        // Chuỗi kết nối không parse được: nếu multiplexer bị resolve khi Provider = Memory thì cache đã ghi Warning
        var logger = new CapturingLogger<StrategyDecisionCache>();
        using var provider = CreateHostlessProvider(
            logger,
            ("Tadss:Cache:Provider", "Memory"),
            ("Tadss:Cache:RedisConnectionString", "localhost:6379,unknownOption=1"));

        var act = () => provider.GetRequiredService<IStrategyDecisionCache>();

        act.Should().NotThrow();
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void AddRuntimeCore_RedisProviderWithExistingMultiplexerRegistration_KeepsExistingOne()
    {
        var existing = new Mock<IConnectionMultiplexer>();
        var services = CreateHostlessServices(
            new CapturingLogger<StrategyDecisionCache>(),
            [("Tadss:Cache:Provider", "Redis")],
            existing.Object);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IConnectionMultiplexer>().Should().BeSameAs(existing.Object);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddRuntimeCore_SharedMemoryCacheWithSizeLimit_StillCaches()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddMemoryCache(options => options.SizeLimit = 100);
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();
        host.Services.GetRequiredService<IMemoryCache>().Should().NotBeNull();
        var cache = host.Services.GetRequiredService<IStrategyDecisionCache>();
        var decision = StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]);

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
    }
}
