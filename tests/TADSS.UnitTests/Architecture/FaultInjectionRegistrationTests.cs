using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.FaultInjection;
using TADSS.Infrastructure.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-09 (A-07): decorator tiêm lỗi chỉ được bọc khi môi trường là <c>Testing</c> <b>và</b> <c>Tadss:Testing:FaultInjection:Enabled = true</c>;
/// ở <c>Production</c>/<c>Staging</c>/<c>Development</c> thì không bao giờ có mặt trong registry dù cấu hình bật. Options + validator luôn đăng ký
/// (UT-14) nên cấu hình sai chặn khởi động ở mọi môi trường.
/// </summary>
public class FaultInjectionRegistrationTests
{
    private static ServiceProvider BuildProvider(string environmentName, params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRuntimeCore(configuration, environmentName);
        return services.BuildServiceProvider();
    }

    private static string[] DecoratedNames(ServiceProvider provider) =>
        provider.GetRequiredService<IStrategyRegistry>().All
            .Where(registered => registered.Strategy is FaultInjectionStrategyDecorator)
            .Select(registered => registered.Strategy.StrategyName)
            .ToArray();

    /// <summary>Ngoài <c>Testing</c> với <c>Enabled = true</c> (kể cả có <c>Profile</c>): không <c>RegisteredStrategy</c> nào là decorator.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void Host_ProductionWithEnabledTrue_DoesNotDecorate_Theory(string environmentName)
    {
        using var provider = BuildProvider(
            environmentName,
            ("Tadss:Testing:FaultInjection:Enabled", "true"),
            ("Tadss:Testing:FaultInjection:Profile", "Throw"));

        DecoratedNames(provider).Should().BeEmpty();
        provider.GetRequiredService<IStrategyRegistry>().All.Should().HaveCount(2);
    }

    /// <summary>Môi trường <c>Testing</c> nhưng <c>Enabled = false</c> (mặc định): không bọc.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Host_TestingWithEnabledFalse_DoesNotDecorate()
    {
        using var provider = BuildProvider("Testing", ("Tadss:Testing:FaultInjection:Profile", "Throw"));

        DecoratedNames(provider).Should().BeEmpty();
    }

    /// <summary>Môi trường <c>Testing</c>, <c>Enabled = true</c>, không chỉ định strategy: mọi strategy đều bị bọc.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Host_TestingWithEnabledTrueAndNoTarget_DecoratesEveryStrategy()
    {
        using var provider = BuildProvider("Testing", ("Tadss:Testing:FaultInjection:Enabled", "true"), ("Tadss:Testing:FaultInjection:Profile", "Throw"));

        DecoratedNames(provider).Should().BeEquivalentTo("DefaultDecisionStrategy", "MockAlternativeDecisionStrategy");
    }

    /// <summary>Môi trường <c>Testing</c>, <c>Enabled = true</c>, <c>TargetStrategy</c> (không phân biệt hoa thường): chỉ strategy đó được bọc, fingerprint giữ nguyên.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Host_TestingWithEnabledTrueAndTarget_DecoratesOnlyTarget()
    {
        using var plain = BuildProvider("Testing");
        using var provider = BuildProvider(
            "Testing",
            ("Tadss:Testing:FaultInjection:Enabled", "true"),
            ("Tadss:Testing:FaultInjection:Profile", "Delay"),
            ("Tadss:Testing:FaultInjection:TargetStrategy", "mockalternativedecisionstrategy"));

        DecoratedNames(provider).Should().Equal("MockAlternativeDecisionStrategy");
        var plainFingerprints = plain.GetRequiredService<IStrategyRegistry>().All.ToDictionary(r => r.Strategy.StrategyName, r => r.OptionsFingerprint);
        provider.GetRequiredService<IStrategyRegistry>().All
            .Should().OnlyContain(registered => registered.OptionsFingerprint == plainFingerprints[registered.Strategy.StrategyName]);
    }

    /// <summary>Options + validator luôn đăng ký: <c>DelayMs</c> sai làm host không khởi động ở mọi môi trường (kể cả khi <c>Enabled = false</c>).</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task ValidateOnStart_InvalidDelayMs_BlocksHostStartup_Theory(string environmentName)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environmentName });
        builder.Configuration.AddInMemoryCollection(
            [new KeyValuePair<string, string?>("Tadss:Testing:FaultInjection:DelayMs", "0"), TestConnectionStrings.Pair]);
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration, environmentName);
        using var host = builder.Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*Tadss:Testing:FaultInjection:DelayMs*");
    }
}
