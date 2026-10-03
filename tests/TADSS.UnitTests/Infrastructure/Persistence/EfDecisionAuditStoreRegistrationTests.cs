using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TADSS.Application.Abstractions.Persistence;
using TADSS.UnitTests.TestDoubles;
using TADSS.Infrastructure.Persistence;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-10/UT-14: <c>AddRuntimeCore</c> đăng ký <see cref="IDecisionEvaluationStore"/> và <see cref="IDecisionQueryStore"/> cùng trỏ về một
/// <see cref="EfDecisionAuditStore"/> với vòng đời scoped (cùng scope dùng chung một instance/DbContext, khác scope thì khác instance).
/// </summary>
public class EfDecisionAuditStoreRegistrationTests
{
    private static IHost BuildHost()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tadss:Cache:Provider"] = "Memory", [TestConnectionStrings.Key] = TestConnectionStrings.Value });
        builder.Services.AddLogging();
        builder.Services.AddDbContext<TadssDbContext>(options => options.UseNpgsql(TestConnectionStrings.Value));
        builder.Services.AddRuntimeCore(builder.Configuration);
        return builder.Build();
    }

    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void AddRuntimeCore_RegistersEfAuditStoreAsScoped()
    {
        using var host = BuildHost();

        using var firstScope = host.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();
        using var secondScope = host.Services.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();

        first.Should().BeOfType<EfDecisionAuditStore>();
        sameScope.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
    }

    /// <summary>Hai port forward tới cùng một <see cref="EfDecisionAuditStore"/> (cùng DbContext) trong một scope, và khác instance giữa hai scope.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AuditStores_ResolveSameScopedInstance()
    {
        using var host = BuildHost();

        using var firstScope = host.Services.CreateScope();
        var evaluation = firstScope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();
        var query = firstScope.ServiceProvider.GetRequiredService<IDecisionQueryStore>();
        var concrete = firstScope.ServiceProvider.GetRequiredService<EfDecisionAuditStore>();
        var governance = firstScope.ServiceProvider.GetRequiredService<IDecisionGovernanceStore>();
        using var secondScope = host.Services.CreateScope();
        var otherEvaluation = secondScope.ServiceProvider.GetRequiredService<IDecisionEvaluationStore>();
        var otherQuery = secondScope.ServiceProvider.GetRequiredService<IDecisionQueryStore>();

        evaluation.Should().BeOfType<EfDecisionAuditStore>();
        query.Should().BeSameAs(evaluation);
        concrete.Should().BeSameAs(evaluation);
        otherQuery.Should().BeSameAs(otherEvaluation);
        otherEvaluation.Should().NotBeSameAs(evaluation);
        // Port ghi quản trị là adapter riêng (EfDecisionGovernanceStore), cũng scoped
        governance.Should().BeOfType<EfDecisionGovernanceStore>();
        firstScope.ServiceProvider.GetRequiredService<IDecisionGovernanceStore>().Should().BeSameAs(governance);
        secondScope.ServiceProvider.GetRequiredService<IDecisionGovernanceStore>().Should().NotBeSameAs(governance);
    }
}
