using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Configuration;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Resilience;

/// <summary>
/// UT-07 / UT-10: nối DI của pipeline v2 trong <c>AddDecisionRuntimeV2</c> — ngân sách thời gian sai chặn khởi động
/// (<c>ValidateOnStart</c>), cấu hình <c>appsettings.json</c> mặc định hợp lệ và <see cref="IStrategyPipeline"/> là
/// singleton resolve được.
/// </summary>
public class ResilienceStartupTests
{
    private static HostApplicationBuilder CreateBuilder(params (string Key, string Value)[] values)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        builder.Services.AddLogging();
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        return builder;
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task ValidateOnStart_BudgetExceedsOuter_BlocksHostStartup()
    {
        using var host = CreateBuilder(
            ("Tadss:Resilience:InnerTimeoutMs", "200"),
            ("Tadss:Resilience:OuterTimeoutMs", "500")).Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*OuterTimeoutMs*");
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public async Task ValidateOnStart_InvalidFailureRatio_BlocksHostStartup()
    {
        using var host = CreateBuilder(("Tadss:Resilience:CircuitBreakerFailureRatio", "0")).Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*CircuitBreakerFailureRatio*");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public async Task AddDecisionRuntimeV2_DefaultAppsettings_HostStartsAndResolvesSingletonPipeline()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddJsonFile(RepositoryPaths.Resolve("src", "TADSS.Runtime.Api", "appsettings.json"), optional: false);
        builder.Services.AddLogging();
        builder.Services.AddDecisionRuntimeV2(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var first = host.Services.GetRequiredService<IStrategyPipeline>();
        var second = host.Services.GetRequiredService<IStrategyPipeline>();
        first.Should().BeSameAs(second);
        host.Services.GetRequiredService<IOptions<ResilienceOptions>>().Value.OuterTimeoutMs.Should().Be(500);
    }
}
