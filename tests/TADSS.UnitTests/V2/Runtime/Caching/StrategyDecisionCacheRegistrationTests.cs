using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Shared.DecisionContractSDK.V2.Decisions;
using StackExchange.Redis;
using TADSS.Runtime.Api.V2.Configuration;
using TADSS.Runtime.Api.V2.Infrastructure.Caching;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Caching;

/// <summary>
/// UT-13: nối DI của cache v2 trong <c>AddDecisionRuntimeV2</c> — <see cref="IStrategyDecisionCache"/> là singleton,
/// tự đủ phụ thuộc (không cần v1 đăng ký <c>IMemoryCache</c>/<c>TadssCacheOptions</c>), dùng multiplexer Redis nếu có và
/// chỉ khi <c>Provider = Redis</c> (không kết nối Redis khi dùng provider Memory).
/// </summary>
public class StrategyDecisionCacheRegistrationTests
{
    private const string Key = "tadss:v2:Default:2.0.0:a1b2c3d4e5f60718:9f86d081884c7d659a2feaa0c55ad015";

    private static HostApplicationBuilder CreateBuilder(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        builder.Services.AddLogging();
        return builder;
    }

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
    public async Task AddDecisionRuntimeV2_WithoutV1Registrations_ResolvesSingletonCache()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
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
    public async Task AddDecisionRuntimeV2_RedisProviderWithRegisteredMultiplexer_WritesToRedis()
    {
        var database = new Mock<IDatabase>();
        database
            .Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        var multiplexer = CreateMultiplexer(database);
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Redis"));
        builder.Services.AddSingleton(multiplexer.Object);
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        using var host = builder.Build();
        var cache = host.Services.GetRequiredService<IStrategyDecisionCache>();

        await cache.SetAsync(Key, StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]), TimeSpan.FromMinutes(60), CancellationToken.None);

        database.Verify(
            db => db.StringSetAsync((RedisKey)Key, It.IsAny<RedisValue>(), TimeSpan.FromMinutes(60), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void AddDecisionRuntimeV2_MemoryProvider_DoesNotResolveMultiplexer()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddSingleton<IConnectionMultiplexer>(
            _ => throw new InvalidOperationException("Provider Memory không được kết nối Redis"));
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        using var host = builder.Build();

        var act = () => host.Services.GetRequiredService<IStrategyDecisionCache>();

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddDecisionRuntimeV2_RedisProviderWithoutMultiplexer_FallsBackToMemory()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Redis"));
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        using var host = builder.Build();
        var cache = host.Services.GetRequiredService<IStrategyDecisionCache>();
        var decision = StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]);

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
    }

    [Fact]
    [Trait("CatalogId", "UT-13")]
    public async Task AddDecisionRuntimeV2_SharedMemoryCacheWithSizeLimit_StillCaches()
    {
        var builder = CreateBuilder(("Tadss:Cache:Provider", "Memory"));
        builder.Services.AddMemoryCache(options => options.SizeLimit = 100);
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        using var host = builder.Build();
        host.Services.GetRequiredService<IMemoryCache>().Should().NotBeNull();
        var cache = host.Services.GetRequiredService<IStrategyDecisionCache>();
        var decision = StrategyDecision.Reject("POLICY_REJECTED", ["Vi phạm"]);

        await cache.SetAsync(Key, decision, TimeSpan.FromMinutes(5), CancellationToken.None);

        (await cache.GetAsync(Key, CancellationToken.None)).Should().BeSameAs(decision);
    }
}
