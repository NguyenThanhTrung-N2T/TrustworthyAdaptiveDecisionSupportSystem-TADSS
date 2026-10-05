using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog.Events;
using TADSS.Application.Routing;
using TADSS.Application.Strategies;
using TADSS.UnitTests.TestDoubles;
using TADSS.Runtime.Api.Composition;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-04 / UT-10: nối DI của registry strategy v2 và router (<c>AddRuntimeCore</c>) và chứng minh cấu hình sai chặn khởi
/// động — <c>ValidateOnStart</c> chặn <c>Host.StartAsync</c> với options sai, còn tham số strategy sai làm registry
/// không dựng được và tên strategy lạ trong <c>Tadss:Routing</c> làm router không dựng được (<c>StartupTasks</c> resolve sớm nên ứng dụng
/// không khởi động). Khóa cũ <c>Tadss:Strategy:ActiveStrategyName</c> chỉ sinh Warning (A-03).
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
        services.AddRuntimeCore(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddRuntimeCore_DefaultAppsettings_ResolvesRegistryWithBothStrategies()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false)
            .Build();
        using var provider = BuildProvider(configuration);

        var registry = provider.GetRequiredService<IStrategyRegistry>();

        registry.All.Select(registered => registered.Strategy.StrategyName)
            .Should().BeEquivalentTo("DefaultDecisionStrategy", "MockAlternativeDecisionStrategy");
        var route = provider.GetRequiredService<IStrategyRouter>().Route(DomainTestData.CreateReservation());
        route.Strategy.Strategy.StrategyName.Should().Be("DefaultDecisionStrategy");
        route.RuleName.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddRuntimeCore_AppsettingsDefaults_EqualPocoDefaults()
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
    public void AddRuntimeCore_RoutingDefaultStrategyConfigured_RouterUsesIt()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Routing:DefaultStrategy", "MockAlternativeDecisionStrategy")));

        provider.GetRequiredService<IStrategyRouter>().Route(DomainTestData.CreateReservation()).Strategy.Strategy.StrategyName
            .Should().Be("MockAlternativeDecisionStrategy");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddRuntimeCore_InvalidStrategyOptions_RegistryResolutionThrowsNamingSection()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Strategies:Default:GoldDepositRate", "1.5")));

        var act = () => provider.GetRequiredService<IStrategyRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tadss:Strategies:Default*");
    }

    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void AddRuntimeCore_UnknownRoutingStrategy_RouterResolutionThrows()
    {
        using var provider = BuildProvider(InMemory(("Tadss:Routing:DefaultStrategy", "Foo")));

        var act = () => provider.GetRequiredService<IStrategyRouter>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Foo*");
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task ValidateOnStart_InvalidOptions_BlocksHostStartup()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Approval:WindowMinutes"] = "0", [TestConnectionStrings.Key] = TestConnectionStrings.Value });
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
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
        builder.Services.AddRuntimeCore(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IStrategyRouter>().Route(DomainTestData.CreateReservation()).Should().NotBeNull();
    }

    /// <summary>
    /// Host thật (<c>Program.cs</c> + <c>StartupTasks</c>) với <c>Tadss:Routing</c> sai: lỗi validator (rule không điều kiện), tên strategy lạ (validator
    /// không thấy registry, router chặn khi <c>StartupTasks</c> resolve sớm) và phần tử danh sách sai kiểu mà binder âm thầm bỏ. Cả ba: host không khởi động,
    /// thông báo nêu khóa/tên sai.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-04")]
    [InlineData("validator", "DefaultDecisionStrategy", null, null, "*Tadss:Routing:Rules:0:When*")]
    [InlineData("unknown-strategy", "Foo", "true", null, "*Foo*")]
    [InlineData("unconvertible-channel", "MockAlternativeDecisionStrategy", "true", "PHONE", "*Tadss:Routing:Rules:0:When:Channels:0*")]
    public void Host_InvalidRouting_FailsOnStart_Theory(string scenario, string strategy, string? peakHour, string? channel, string expectedMessage)
    {
        using var baseFactory = new DecisionsApiFactory();
        var routing = new Dictionary<string, string?>
        {
            ["Tadss:Routing:Rules:0:Name"] = "rule-under-test",
            ["Tadss:Routing:Rules:0:Strategy"] = strategy,
            ["Tadss:Routing:Rules:0:When:PeakHour"] = peakHour,
        };
        if (channel is not null)
        {
            routing["Tadss:Routing:Rules:0:When:Channels:0"] = channel;
        }

        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(routing)));

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>("kịch bản {0} phải chặn khởi động", scenario)
            .Where(exception => exception is OptionsValidationException || exception is InvalidOperationException)
            .WithMessage(expectedMessage);
    }

    /// <summary>
    /// A-03: cấu hình cuối cùng còn khóa cũ <c>Tadss:Strategy:ActiveStrategyName</c> → host vẫn khởi động (<c>/health/live</c> trả 200), có đúng một
    /// Warning nêu khóa cũ và khóa thay thế, và giá trị của khóa cũ không xuất hiện trong bất kỳ dòng log nào.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public async Task Startup_LegacyActiveStrategyNameKey_LogsWarning()
    {
        const string legacyValue = "LegacyValue-7f3a9c";
        var sink = new CapturingLogEventSink();
        using var baseFactory = new DecisionsApiFactory();
        using var withLegacyKey = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new("Tadss:Strategy:ActiveStrategyName", legacyValue)])));
        using var factory = CapturingLogEventSink.Attach(withLegacyKey, sink);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.IsSuccessStatusCode.Should().BeTrue("khóa cũ không được dừng khởi động");
        sink.Events
            .Where(logEvent => logEvent.Level == LogEventLevel.Warning
                && logEvent.RenderMessage().Contains("Tadss:Strategy:ActiveStrategyName", StringComparison.Ordinal)
                && logEvent.RenderMessage().Contains("Tadss:Routing:DefaultStrategy", StringComparison.Ordinal))
            .Should().ContainSingle();
        sink.Events.Should().NotContain(logEvent => CapturingLogEventSink.FullText(logEvent).Contains(legacyValue, StringComparison.Ordinal));
    }

    /// <summary>Không có khóa cũ thì không có Warning nào về khóa cũ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-04")]
    public void Startup_WithoutLegacyKey_NoLegacyWarning()
    {
        var sink = new CapturingLogEventSink();
        using var baseFactory = new DecisionsApiFactory();
        using var factory = CapturingLogEventSink.Attach(baseFactory, sink);
        using var client = factory.CreateClient();

        sink.Events.Should().NotBeEmpty("host phải ghi log khởi động vào sink");
        sink.Events.Where(logEvent => logEvent.RenderMessage().Contains("Tadss:Strategy:ActiveStrategyName", StringComparison.Ordinal))
            .Should().BeEmpty();
    }
}
