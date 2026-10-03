using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Decisions;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Data;
using Xunit;

namespace TADSS.UnitTests.Application.Decisions;

/// <summary>
/// UT-10: <c>AddDecisionRuntime</c> đăng ký <see cref="IDecisionQueryService"/> (<see cref="DecisionQueryService"/>) và
/// <see cref="IDecisionService"/> (<see cref="DecisionService"/>) với vòng đời scoped (cùng scope dùng chung một instance, khác scope thì khác instance) vì phụ thuộc kho audit scoped.
/// </summary>
public class DecisionQueryServiceRegistrationTests
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

    /// <summary>Resolve được ở cấp scope, scoped đúng nghĩa và đúng kiểu hiện thực.</summary>
    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddDecisionRuntime_RegistersQueryServiceAsScoped()
    {
        using var host = BuildHost();

        using var firstScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDecisionQueryService>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IDecisionQueryService>();
        using var secondScope = host.Services.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IDecisionQueryService>();

        first.Should().BeOfType<DecisionQueryService>();
        sameScope.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
    }

    /// <summary>Resolve được <see cref="IDecisionService"/> (kéo theo kho audit scoped, strategy, pipeline, cache singleton) với vòng đời scoped.</summary>
    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddDecisionRuntime_RegistersDecisionServiceAsScoped()
    {
        using var host = BuildHost();

        using var firstScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDecisionService>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IDecisionService>();
        using var secondScope = host.Services.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IDecisionService>();

        first.Should().BeOfType<DecisionService>();
        sameScope.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
    }
}
