using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Decisions;
using TADSS.UnitTests.TestDoubles;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-10: nối DI của <see cref="DecisionResponseComposer"/> trong <c>AddRuntimeCore</c> — composer là singleton
/// resolve được (việc đăng ký kho audit được kiểm ở <c>EfDecisionAuditStoreRegistrationTests</c>).
/// </summary>
public class DecisionResponseComposerRegistrationTests
{
    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Cache:Provider"] = "Memory", [TestConnectionStrings.Key] = TestConnectionStrings.Value });
        builder.Services.AddLogging();
        builder.Services.AddRuntimeCore(builder.Configuration);
        return builder;
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddRuntimeCore_ResolvesSingletonComposer()
    {
        using var host = CreateBuilder().Build();

        var first = host.Services.GetRequiredService<DecisionResponseComposer>();
        var second = host.Services.GetRequiredService<DecisionResponseComposer>();

        first.Should().BeSameAs(second);
    }
}
