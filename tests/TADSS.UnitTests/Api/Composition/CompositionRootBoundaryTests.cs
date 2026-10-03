using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14: chỉ composition root (<c>Program.cs</c> và thư mục <c>Composition/</c>) được dùng kiểu của <c>TADSS.Infrastructure</c>;
/// controller, middleware, error handling và OpenAPI của Runtime.Api chỉ thấy Application và SDK (spec 4.2.4).
/// </summary>
public class CompositionRootBoundaryTests
{
    private const string InfrastructureNamespace = "TADSS.Infrastructure";

    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Program_HasNoInfrastructureUsingsOutsideCompositionRoot()
    {
        var apiRoot = RepositoryPaths.Resolve("src", "TADSS.Runtime.Api");

        var violations = Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(apiRoot, path))
            .Where(relative => !IsBuildOutput(relative) && !IsCompositionRoot(relative))
            .Where(relative => File.ReadAllText(Path.Combine(apiRoot, relative)).Contains(InfrastructureNamespace, StringComparison.Ordinal))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty($"chỉ Program.cs và Composition/ được dùng '{InfrastructureNamespace}'");
    }

    private static bool IsBuildOutput(string relative)
    {
        return Parts(relative).Any(part => part is "bin" or "obj");
    }

    private static bool IsCompositionRoot(string relative)
    {
        var parts = Parts(relative);
        return parts.Length == 1 && parts[0] == "Program.cs" || parts[0] == "Composition";
    }

    private static string[] Parts(string relative)
    {
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
