using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Services.Decisions;
using Xunit;

namespace TADSS.UnitTests.Services;

/// <summary>
/// UT-10: nối DI của <see cref="DecisionResponseComposer"/> trong <c>AddDecisionRuntime</c> — composer là singleton
/// resolve được; <see cref="IDecisionAuditStore"/> chưa được đăng ký ở 5.2b (B-09: <c>EfDecisionAuditStore</c> thuộc 5.2c).
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

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddDecisionRuntime_DoesNotRegisterAuditStore()
    {
        using var host = CreateBuilder().Build();

        host.Services.GetService<IDecisionAuditStore>().Should().BeNull();
    }
}
