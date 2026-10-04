using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// SEC-5: health check Redis chỉ làm readiness <c>Degraded</c> (không bao giờ <c>Unhealthy</c>) khi không có multiplexer, không tạo được kết nối,
/// <c>PING</c> lỗi hoặc quá timeout; description và log không chứa chuỗi kết nối hay message của exception (chỉ tên loại exception trong log).
/// Check chỉ được đăng ký khi <c>Provider = Redis</c>, với tag <c>ready</c> và <c>failureStatus = Degraded</c>.
/// </summary>
public class RedisHealthCheckTests
{
    private const string SecretMessage = "Host=redis.internal,password=s3cr3t-value";

    private readonly CapturingLogger<RedisHealthCheck> _logger = new();

    /// <summary>Không đăng ký <c>IConnectionMultiplexer</c> nào: <c>Degraded</c>, description không chứa dữ liệu kết nối, không ghi ngoại lệ.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task CheckHealth_NoMultiplexer_IsDegraded()
    {
        var check = CreateCheck(new ServiceCollection().BuildServiceProvider());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeNull();
        result.Description.Should().NotContainAny("Host=", "password", "redis.internal", "Exception");
    }

    /// <summary>Tạo multiplexer ném lỗi chứa chuỗi kết nối: <c>Degraded</c>; message không lọt vào description hay log, chỉ có tên loại exception trong log.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task CheckHealth_MultiplexerCreationFails_IsDegradedWithoutLeak()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ => throw new InvalidOperationException(SecretMessage));
        var check = CreateCheck(services.BuildServiceProvider());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        AssertDegradedWithoutLeak(result, "InvalidOperationException");
    }

    /// <summary><c>PING</c> ném lỗi mang chuỗi kết nối: <c>Degraded</c>, không lộ message.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task CheckHealth_PingFails_IsDegradedWithoutLeak()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.PingAsync(It.IsAny<CommandFlags>())).ThrowsAsync(new TimeoutException(SecretMessage));
        var check = CreateCheck(ServicesWith(database.Object));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        AssertDegradedWithoutLeak(result, "TimeoutException");
    }

    /// <summary><c>PING</c> không bao giờ trả về: check dừng ở <c>RedisConnectTimeoutMs</c> và cho <c>Degraded</c>.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task CheckHealth_PingNeverCompletes_IsDegradedAfterTimeout()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.PingAsync(It.IsAny<CommandFlags>())).Returns(new TaskCompletionSource<TimeSpan>().Task);
        var check = CreateCheck(ServicesWith(database.Object), redisConnectTimeoutMs: 50);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        AssertDegradedWithoutLeak(result, "TimeoutException");
    }

    /// <summary><c>PING</c> thành công: <c>Healthy</c>.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-5")]
    public async Task CheckHealth_PingSucceeds_IsHealthy()
    {
        var database = new Mock<IDatabase>();
        database.Setup(db => db.PingAsync(It.IsAny<CommandFlags>())).ReturnsAsync(TimeSpan.FromMilliseconds(1));
        var check = CreateCheck(ServicesWith(database.Object));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    /// <summary><c>Provider = Redis</c> (và mặc định): đăng ký check <c>redis</c> với tag <c>ready</c>, <c>failureStatus = Degraded</c>; <c>Provider = Memory</c>: không đăng ký.</summary>
    [Theory]
    [Trait("CatalogId", "SEC-5")]
    [InlineData("Redis", true)]
    [InlineData("redis", true)]
    [InlineData(null, true)]
    [InlineData("Memory", false)]
    public void AddCacheHealthChecks_RegistersRedisCheckOnlyForRedisProvider_Theory(string? provider, bool expectRegistered)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(provider is null ? [] : [new KeyValuePair<string, string?>($"{CacheOptions.SectionName}:Provider", provider)])
            .Build();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddCacheHealthChecks(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        var registrations = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        if (expectRegistered)
        {
            var registration = registrations.Should().ContainSingle().Subject;
            registration.Name.Should().Be("redis");
            registration.FailureStatus.Should().Be(HealthStatus.Degraded);
            registration.Tags.Should().BeEquivalentTo(["ready"]);
        }
        else
        {
            registrations.Should().BeEmpty();
        }
    }

    private void AssertDegradedWithoutLeak(HealthCheckResult result, string expectedExceptionType)
    {
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeNull();
        result.Description.Should().NotContainAny("Host=", "password", "redis.internal", "s3cr3t", expectedExceptionType);
        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(expectedExceptionType));
        _logger.Entries.Should().OnlyContain(entry => entry.Exception == null && !entry.Message.Contains("s3cr3t") && !entry.Message.Contains("Host="));
    }

    private static IServiceProvider ServicesWith(IDatabase database)
    {
        var multiplexer = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database);
        var services = new ServiceCollection();
        services.AddSingleton(multiplexer.Object);
        return services.BuildServiceProvider();
    }

    private RedisHealthCheck CreateCheck(IServiceProvider services, int redisConnectTimeoutMs = 3000) =>
        new(services, Microsoft.Extensions.Options.Options.Create(new CacheOptions { RedisConnectTimeoutMs = redisConnectTimeoutMs }), _logger);
}
