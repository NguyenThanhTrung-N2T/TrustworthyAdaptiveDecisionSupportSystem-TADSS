using System.Reflection;
using FluentAssertions;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// Kiến trúc & Ranh giới Chiến lược Thẩm định (Strategy Dependency Purity & Isolation Tests).
/// <para>
/// Khẳng định:
/// 1. Các project Strategy (Default, MockAlternative) chỉ phụ thuộc vào Contract SDK và BCL (.NET 8).
/// 2. Các project Strategy tuyệt đối KHÔNG phụ thuộc vào Runtime.Api, Web, Persistence (EF/Npgsql), Cache (Redis), hoặc Resilience (Polly).
/// 3. Toàn bộ Strategy implementations là Stateless và Thread-safe.
/// </para>
/// </summary>
public class StrategyDependencyPurityTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "TADSS.Runtime",
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Polly",
        "MediatR"
    ];

    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(typeof(DefaultDecisionStrategy))]
    [InlineData(typeof(MockAlternativeDecisionStrategy))]
    public void StrategyAssembly_DoesNotReference_ForbiddenInfrastructureOrRuntimeAssemblies(Type strategyType)
    {
        var strategyAssembly = strategyType.Assembly;

        var referencedAssemblies = strategyAssembly
            .GetReferencedAssemblies()
            .Select(r => r.Name)
            .Where(name => name is not null)
            .ToList();

        // Must reference SDK
        referencedAssemblies.Should().Contain(
            "Shared.DecisionContractSDK",
            $"strategy '{strategyType.Name}' must reference Shared.DecisionContractSDK");

        // Must NOT reference any forbidden infrastructure or runtime assemblies
        foreach (var forbiddenPrefix in ForbiddenAssemblyPrefixes)
        {
            var violatingReferences = referencedAssemblies
                .Where(name => name!.StartsWith(forbiddenPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            violatingReferences.Should().BeEmpty(
                $"Strategy '{strategyType.Name}' must remain purely decoupled from infrastructure/runtime. Found forbidden reference to: {string.Join(", ", violatingReferences)}");
        }
    }

    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(typeof(DefaultDecisionStrategy))]
    [InlineData(typeof(MockAlternativeDecisionStrategy))]
    public void StrategyImplementations_AreStateless_HaveNoMutableFields(Type strategyType)
    {
        var fields = strategyType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(f => !f.IsInitOnly && !f.IsLiteral && !f.Name.Contains("BackingField", StringComparison.OrdinalIgnoreCase))
            .ToList();

        fields.Should().BeEmpty(
            $"Strategy '{strategyType.Name}' must be completely stateless (no mutable fields) to ensure safe Singleton registration. Found mutable fields: {string.Join(", ", fields.Select(f => f.Name))}");
    }
}
