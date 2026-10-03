using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Data;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Data;

/// <summary>
/// UT-10: <c>AddDecisionRuntime</c> đăng ký <see cref="IDecisionAuditStore"/> là <see cref="EfDecisionAuditStore"/> với
/// vòng đời scoped (cùng scope dùng chung một instance/DbContext, khác scope thì khác instance).
/// </summary>
public class EfDecisionAuditStoreRegistrationTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Cache:Provider"] = "Memory" });
        builder.Services.AddLogging();
        builder.Services.AddDbContext<TadssDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Port=1;Database=none"));
        builder.Services.AddDecisionRuntime(builder.Configuration);
        return builder.Build();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddDecisionRuntime_RegistersEfAuditStoreAsScoped()
    {
        using var host = BuildHost();

        using var firstScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDecisionAuditStore>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IDecisionAuditStore>();
        using var secondScope = host.Services.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IDecisionAuditStore>();

        first.Should().BeOfType<EfDecisionAuditStore>();
        sameScope.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
    }
}
