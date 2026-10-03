using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Decisions;
using TADSS.Runtime.Api.Configuration;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-10: nối DI của <see cref="DecisionResponseComposer"/> trong <c>AddDecisionRuntime</c> — composer là singleton
/// resolve được (việc đăng ký kho audit được kiểm ở <c>EfDecisionAuditStoreRegistrationTests</c>).
/// </summary>
public class DecisionResponseComposerRegistrationTests
{
    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Cache:Provider"] = "Memory" });
        builder.Services.AddLogging();
        builder.Services.AddDecisionRuntime(builder.Configuration);
        return builder;
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddDecisionRuntime_ResolvesSingletonComposer()
    {
        using var host = CreateBuilder().Build();

        var first = host.Services.GetRequiredService<DecisionResponseComposer>();
        var second = host.Services.GetRequiredService<DecisionResponseComposer>();

        first.Should().BeSameAs(second);
    }
}
