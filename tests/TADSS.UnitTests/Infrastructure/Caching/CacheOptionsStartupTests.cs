using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Caching;

/// <summary>
/// UT-13: <see cref="CacheOptionsValidator"/> được nối trong <c>AddInfrastructure</c> với <c>ValidateOnStart</c> — cấu hình cache sai
/// chặn khởi động, cấu hình đúng (không phân biệt hoa thường ở <c>Provider</c>) đi qua.
/// </summary>
public class CacheOptionsStartupTests
{
    private static IHost BuildHost(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair));
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Provider lạ, timeout thao tác hoặc timeout kết nối không dương, tham số breaker Redis ngoài khoảng hợp lệ hoặc <c>InstanceName</c> sai (rỗng, có khoảng trắng, dài hơn 32 ký tự) đều chặn khởi động và nêu khóa sai.</summary>
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Provider", "Memcached")]
    [InlineData("Provider", "")]
    [InlineData("RedisTimeoutMs", "0")]
    [InlineData("RedisTimeoutMs", "-1")]
    [InlineData("RedisConnectTimeoutMs", "0")]
    [InlineData("RedisConnectTimeoutMs", "-5")]
    [InlineData("RedisBreakerFailureRatio", "0")]
    [InlineData("RedisBreakerFailureRatio", "1.5")]
    [InlineData("RedisBreakerSamplingSeconds", "0")]
    [InlineData("RedisBreakerMinimumThroughput", "1")]
    [InlineData("RedisBreakerBreakDurationSeconds", "0")]
    [InlineData("InstanceName", "")]
    [InlineData("InstanceName", "co khoang trang:")]
    [InlineData("InstanceName", "tien-to-dai-hon-ba-muoi-hai-ky-tu-ABCDEFG")]
    public async Task CacheOptions_Invalid_FailsOnStart_Theory(string key, string value)
    {
        using var host = BuildHost(($"Tadss:Cache:{key}", value));

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage($"*Tadss:Cache:{key}*");
    }

    /// <summary>Provider so sánh không phân biệt hoa thường như phép so sánh trước đây trong cache.</summary>
    [Theory]
    [Trait("CatalogId", "UT-13")]
    [InlineData("Redis")]
    [InlineData("redis")]
    [InlineData("Memory")]
    [InlineData("MEMORY")]
    public async Task CacheOptions_ProviderAnyCasing_StartsHost_Theory(string provider)
    {
        using var host = BuildHost(("Tadss:Cache:Provider", provider));

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IOptions<CacheOptions>>().Value.Provider.Should().Be(provider);
    }

    /// <summary>Mặc định mới tái tạo hành vi cũ: timeout kết nối tối thiểu 3000 ms và provider Redis.</summary>
    [Fact]
    [Trait("CatalogId", "UT-13")]
    public void CacheOptions_Defaults_RecreatePreviousBehavior()
    {
        var options = new CacheOptions();

        options.RedisConnectTimeoutMs.Should().Be(3000);
        options.RedisTimeoutMs.Should().Be(150);
        options.Provider.Should().Be(CacheProviders.Redis);
        CacheOptions.SectionName.Should().Be("Tadss:Cache");
    }
}
