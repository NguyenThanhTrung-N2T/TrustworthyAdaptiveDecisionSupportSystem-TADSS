using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14: ranh giới giữa các tầng Clean Architecture (spec 4.2.4) kiểm bằng assembly và nguồn:
/// Application không tham chiếu hạ tầng/presentation, Infrastructure không tham chiếu presentation,
/// EF Core chỉ nằm trong <c>Persistence/</c>, và port (interface mà Infrastructure hiện thực) chỉ khai báo ở
/// <c>TADSS.Application.Abstractions.*</c>. Ranh giới của Runtime.Api (chỉ composition root dùng Infrastructure) nằm ở
/// <c>CompositionRootBoundaryTests</c>; quy tắc <c>ProjectReference</c>/<c>PackageReference</c> nằm ở <c>ProjectReferenceRulesTests</c>.
/// </summary>
public class LayerDependencyTests
{
    private const string PortNamespace = "TADSS.Application.Abstractions";

    /// <summary>Mẫu tên assembly bị cấm với Application; hậu tố <c>*</c> nghĩa là khớp theo tiền tố.</summary>
    private static readonly string[] ApplicationForbiddenReferences =
    [
        "TADSS.Infrastructure",
        "TADSS.Runtime.Api",
        "TADSS.Strategies.*",
        "Microsoft.AspNetCore.*",
        "Microsoft.EntityFrameworkCore*",
        "Npgsql*",
        "StackExchange.Redis",
        "Polly*",
    ];

    /// <summary>
    /// Mẫu tên assembly bị cấm với Infrastructure (tầng presentation): Runtime.Api, mọi assembly <c>Microsoft.AspNetCore.*</c> và Swashbuckle.
    /// Gói <c>AspNetCore.HealthChecks.NpgSql</c> được phép vì assembly của nó tên <c>HealthChecks.NpgSql</c> và không kéo theo <c>Microsoft.AspNetCore.*</c>.
    /// </summary>
    private static readonly string[] InfrastructureForbiddenReferences =
    [
        "TADSS.Runtime.Api",
        "Microsoft.AspNetCore.*",
        "Swashbuckle*",
    ];

    /// <summary>Application không được tham chiếu hạ tầng, presentation, strategy cụ thể, ASP.NET Core, EF Core, Npgsql, Redis hay Polly.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Application_ReferencesNoInfrastructureAssemblies()
    {
        var violations = ReferencedAssemblyNames(SourceTree.Application, ApplicationForbiddenReferences);

        violations.Should().BeEmpty(
            "TADSS.Application chỉ được phụ thuộc SDK và abstraction Microsoft.Extensions.*; vi phạm: {0}",
            string.Join(", ", violations));
    }

    /// <summary>Infrastructure không được tham chiếu Runtime.Api, ASP.NET Core (<c>Microsoft.AspNetCore.*</c>) hay Swashbuckle.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Infrastructure_ReferencesNoPresentationAssemblies()
    {
        var violations = ReferencedAssemblyNames(SourceTree.Infrastructure, InfrastructureForbiddenReferences);

        violations.Should().BeEmpty(
            "TADSS.Infrastructure không được biết tầng presentation; vi phạm: {0}",
            string.Join(", ", violations));
    }

    /// <summary>Mọi file nguồn của Infrastructure dùng <c>Microsoft.EntityFrameworkCore</c> phải nằm trong <c>Persistence/</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void EfCore_UsedOnlyInPersistence()
    {
        var files = SourceTree.FilesOf(SourceTree.Infrastructure);
        files.Should().NotBeEmpty("test không được pass rỗng khi không đọc được nguồn Infrastructure");

        var violations = files
            .Where(file => file.Folders.FirstOrDefault() != "Persistence")
            .Where(file => file.CodeWithoutComments().Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(file => file.Display)
            .ToList();

        violations.Should().BeEmpty(
            "EF Core chỉ được dùng trong TADSS.Infrastructure/Persistence/; vi phạm: {0}",
            string.Join(", ", violations));
    }

    /// <summary>
    /// Interface của TADSS mà một kiểu trong Infrastructure hiện thực là port, phải khai báo ở
    /// <c>TADSS.Application.Abstractions.*</c> (interface của nền tảng như <c>IHealthCheck</c> hay của SDK không tính).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void PortInterfaces_LiveInApplicationAbstractions()
    {
        var tadssAssemblies = new[] { SourceTree.AssemblyOf(SourceTree.Application), SourceTree.AssemblyOf(SourceTree.Infrastructure) };

        var implemented = SourceTree.AssemblyOf(SourceTree.Infrastructure).GetTypes()
            .Where(type => type is { IsClass: true } && !IsCompilerGenerated(type))
            .SelectMany(type => type.GetInterfaces().Select(@interface => (Implementer: type, Interface: @interface)))
            .Select(pair => (pair.Implementer, Interface: pair.Interface.IsGenericType ? pair.Interface.GetGenericTypeDefinition() : pair.Interface))
            .Where(pair => tadssAssemblies.Contains(pair.Interface.Assembly))
            .ToList();

        implemented.Should().NotBeEmpty("Infrastructure phải hiện thực ít nhất một port của Application");

        var violations = implemented
            .Where(pair => pair.Interface.Namespace is not { } ns
                || !(ns == PortNamespace || ns.StartsWith(PortNamespace + ".", StringComparison.Ordinal)))
            .Select(pair => $"{pair.Interface.FullName} (hiện thực bởi {pair.Implementer.FullName})")
            .Distinct()
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "port mà Infrastructure hiện thực phải khai báo trong {0}.*; vi phạm: {1}",
            PortNamespace,
            string.Join("; ", violations));
    }

    private static List<string> ReferencedAssemblyNames(string project, string[] forbiddenPatterns)
    {
        var references = SourceTree.AssemblyOf(project).GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToList();

        references.Should().NotBeEmpty($"assembly '{project}' phải có tham chiếu để kiểm tra");

        return references
            .Where(name => forbiddenPatterns.Any(pattern => Matches(name, pattern)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool Matches(string assemblyName, string pattern)
    {
        return pattern.EndsWith('*')
            ? assemblyName.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(assemblyName, pattern, StringComparison.Ordinal);
    }

    private static bool IsCompilerGenerated(Type type)
    {
        return type.Name.Contains('<', StringComparison.Ordinal);
    }
}
