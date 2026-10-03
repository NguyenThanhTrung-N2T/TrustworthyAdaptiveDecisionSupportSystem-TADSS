using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;
using TADSS.Infrastructure.Options;
using TADSS.Runtime.Api.Composition;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14: <c>appsettings.json</c> (không nạp <c>appsettings.Development.json</c>) qua mọi validator của runtime khi host khởi động,
/// và mỗi options có tham số kiểm được đều có đúng một validator được đăng ký cùng <c>ValidateOnStart</c>.
/// <c>DatabaseOptions</c> không có validator vì chỉ có một cờ <c>bool</c>, không có giá trị nào để kiểm.
/// </summary>
public class AppSettingsValidationTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false);
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        builder.Services.AddPresentation(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Không options nào lỗi khi khởi động và mọi giá trị đọc ra khớp mặc định đã chốt.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public async Task AppSettings_PassAllValidators()
    {
        using var host = BuildHost();

        await host.StartAsync();
        await host.StopAsync();

        var services = host.Services;
        services.GetRequiredService<IOptions<ApprovalOptions>>().Value.WindowMinutes.Should().Be(30);
        services.GetRequiredService<IOptions<ValidationOptions>>().Value.MaxItemMinutes.Should().Be(720);
        services.GetRequiredService<IOptions<CalendarOptions>>().Value.Should().NotBeNull();
        services.GetRequiredService<IOptions<StrategyOptions>>().Value.ActiveStrategyName.Should().Be("DefaultDecisionStrategy");
        services.GetRequiredService<IOptions<ResilienceOptions>>().Value.OuterTimeoutMs.Should().Be(500);
        var cache = services.GetRequiredService<IOptions<CacheOptions>>().Value;
        cache.Provider.Should().Be(CacheProviders.Redis);
        cache.RedisConnectTimeoutMs.Should().Be(3000);
        services.GetRequiredService<IOptions<ErrorHandlingOptions>>().Value.RetryAfterSeconds.Should().Be(5);
    }

    /// <summary>Mỗi options có tham số kiểm được đều có validator đăng ký.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AppSettings_EveryValidatableOptions_HasRegisteredValidator()
    {
        using var host = BuildHost();
        var services = host.Services;

        services.GetServices<IValidateOptions<ApprovalOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<ValidationOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<CalendarOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<StrategyOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<ResilienceOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<CacheOptions>>().Should().ContainSingle();
        services.GetServices<IValidateOptions<ErrorHandlingOptions>>().Should().ContainSingle();
    }
}
