using FluentAssertions;
using TADSS.Infrastructure.Caching;
using TADSS.Infrastructure.Options;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: timeout kết nối Redis = <c>max(RedisTimeoutMs, RedisConnectTimeoutMs)</c> với host thật, còn host placeholder của template cấu hình
/// dùng <c>RedisTimeoutMs</c>. Chỉ dựng <c>ConfigurationOptions</c> nên không mở kết nối nào.
/// </summary>
public class RedisConnectionFactoryTests
{
    private const string RealHostConnectionString = "localhost:6379";
    private const string PlaceholderConnectionString = "your-redis-host:11082,user=default,password=your-redis-password";

    /// <summary>Host thật: <c>max(150, 3000) = 3000</c> và <c>max(5000, 3000) = 5000</c>; host placeholder luôn dùng <c>RedisTimeoutMs</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData(RealHostConnectionString, 150, 3000, 3000)]
    [InlineData(RealHostConnectionString, 5000, 3000, 5000)]
    [InlineData(PlaceholderConnectionString, 150, 3000, 150)]
    [InlineData(PlaceholderConnectionString, 5000, 3000, 5000)]
    public void RedisConnectionFactory_ConnectTimeout_UsesMaxOfOptions_Theory(
        string connectionString, int redisTimeoutMs, int redisConnectTimeoutMs, int expectedConnectTimeout)
    {
        var options = new CacheOptions
        {
            RedisConnectionString = connectionString,
            RedisTimeoutMs = redisTimeoutMs,
            RedisConnectTimeoutMs = redisConnectTimeoutMs,
        };

        var configuration = RedisConnectionFactory.BuildConfiguration(options);

        configuration.ConnectTimeout.Should().Be(expectedConnectTimeout);
        configuration.SyncTimeout.Should().Be(redisTimeoutMs);
        configuration.AsyncTimeout.Should().Be(redisTimeoutMs);
        configuration.AbortOnConnectFail.Should().BeFalse();
    }

    /// <summary>Đổi <c>RedisConnectTimeoutMs</c> thì timeout kết nối đổi theo (không còn số 3000 cố định).</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void RedisConnectionFactory_CustomConnectTimeout_IsHonored()
    {
        var options = new CacheOptions { RedisConnectionString = RealHostConnectionString, RedisConnectTimeoutMs = 8000 };

        RedisConnectionFactory.BuildConfiguration(options).ConnectTimeout.Should().Be(8000);
    }
}
