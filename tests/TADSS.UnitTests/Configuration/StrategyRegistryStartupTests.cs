using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Application.Strategies;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Services.Strategies;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Configuration;

/// <summary>
/// UT-04 / UT-10: nối DI của registry strategy v2 (<c>AddDecisionRuntime</c>) và chứng minh cấu hình sai chặn khởi
/// động — <c>ValidateOnStart</c> chặn <c>Host.StartAsync</c> với options sai, còn tham số strategy sai làm registry
/// không dựng được (<c>Program.cs</c> resolve sớm nên ứng dụng không khởi động).
/// </summary>
public class StrategyRegistryStartupTests
{
    private static IConfiguration InMemory(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)).Append(TestConnectionStrings.Pair))
            .Build();

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDecisionRuntime(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddDecisionRuntime_DefaultAppsettings_ResolvesRegistryWithBothStrategies()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();
        using var provider = BuildProvider(configuration);

        var registry = provider.GetRequiredService<IStrategyRegistry>();

        registry.All.Select(registered => registered.Strategy.StrategyName)
            .Should().BeEquivalentTo("DefaultDecisionStrategy", "MockAlternativeDecisionStrategy");
        registry.ResolveActive().Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddDecisionRuntime_AppsettingsDefaults_EqualPocoDefaults()
    {
        var calendar = DomainTestData.StandardCalendar();
        var appsettings = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();
        var empty = new ConfigurationBuilder().Build();

        StrategyRegistrations.CreateDefault(appsettings, calendar).OptionsFingerprint
            .Should().Be(StrategyRegistrations.CreateDefault(empty, calendar).OptionsFingerprint);
        StrategyRegistrations.CreateMockAlternative(appsettings, calendar).OptionsFingerprint
            .Should().Be(StrategyRegistrations.CreateMockAlternative(empty, calendar).OptionsFingerprint);
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddDecisionRuntime_ActiveStrategyConfigured_RegistryUsesIt()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Strategy:ActiveStrategyName", "MockAlternativeDecisionStrategy")));

        provider.GetRequiredService<IStrategyRegistry>().ResolveActive().Strategy.StrategyName
            .Should().Be("MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddDecisionRuntime_InvalidStrategyOptions_RegistryResolutionThrowsNamingSection()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Strategies:Default:GoldDepositRate", "1.5")));

        var act = () => provider.GetRequiredService<IStrategyRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tadss:Strategies:Default*");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddDecisionRuntime_UnknownActiveStrategy_RegistryResolutionThrows()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Strategy:ActiveStrategyName", "Foo")));

        var act = () => provider.GetRequiredService<IStrategyRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Foo*");
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task ValidateOnStart_InvalidOptions_BlocksHostStartup()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Approval:WindowMinutes"] = "0", [TestConnectionStrings.Key] = TestConnectionStrings.Value });
        builder.Services.AddLogging();
        builder.Services.AddDecisionRuntime(builder.Configuration);
        using var host = builder.Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*WindowMinutes*");
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task ValidateOnStart_ValidOptions_HostStarts()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([TestConnectionStrings.Pair]);
        builder.Services.AddLogging();
        builder.Services.AddDecisionRuntime(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IStrategyRegistry>().ResolveActive().Should().NotBeNull();
    }
}
