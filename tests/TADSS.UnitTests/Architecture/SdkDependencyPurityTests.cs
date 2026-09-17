using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.Architecture;

public class SdkDependencyPurityTests
{
    [Fact]
    public void SharedDecisionContractSdk_ReferencesOnlyBclAssemblies()
    {
        var sdkAssembly = typeof(Reservation).Assembly;

        var externalReferences = sdkAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null
                && !name.StartsWith("System.", StringComparison.Ordinal)
                && !string.Equals(name, "System", StringComparison.Ordinal)
                && !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .OrderBy(name => name)
            .ToArray();

        externalReferences.Should().BeEmpty(
            "the shared contract SDK must remain provider-independent and depend only on BCL assemblies");
    }
}
