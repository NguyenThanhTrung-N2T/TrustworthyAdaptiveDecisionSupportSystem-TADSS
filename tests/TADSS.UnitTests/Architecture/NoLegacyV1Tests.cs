using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application;
using TADSS.Infrastructure;
using TADSS.Runtime.Api.Composition;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-09: không còn dấu vết v1 / cấu trúc tạm <c>V2</c> sau khi chuyển hẳn sang contract v2 — không namespace nào có đoạn
/// <c>V2</c> (ở cả Application, Infrastructure, Runtime.Api, SDK và Strategies), không thư mục nguồn tên <c>V2</c>, không còn kiểu dịch vụ v1 đã gỡ,
/// và mọi namespace của Application, Infrastructure, Runtime.Api ngoài <c>TADSS.Infrastructure.Persistence</c> không phụ thuộc
/// persistence EF hay MediatR (chỉ nói chuyện với hạ tầng qua interface như <c>IDecisionEvaluationStore</c>).
/// </summary>
public class NoLegacyV1Tests
{
    private const string LegacySegment = "V2";

    /// <summary>Tên kiểu dịch vụ v1 đã bị gỡ ở 5.2c — không được xuất hiện lại trong assembly Runtime.</summary>
    private static readonly string[] RemovedV1TypeNames =
        ["IStrategyResolver", "IDecisionResiliencePipeline", "IDecisionCacheService"];

    /// <summary>Namespace của hạ tầng dữ liệu EF: chỉ ở đây mới được dùng <c>TadssDbContext</c>, entity và EF Core.</summary>
    private const string PersistenceNamespace = "TADSS.Infrastructure.Persistence";

    /// <summary>Ba project lớp runtime được quét: Application, Infrastructure, Runtime.Api (mọi namespace của chúng).</summary>
    private static readonly string[] RuntimeLayerProjects = [SourceTree.Application, SourceTree.Infrastructure, SourceTree.RuntimeApi];

    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(typeof(Reservation))]
    [InlineData(typeof(ApplicationServiceCollectionExtensions))]
    [InlineData(typeof(InfrastructureServiceCollectionExtensions))]
    [InlineData(typeof(PresentationServiceCollectionExtensions))]
    [InlineData(typeof(DefaultDecisionStrategy))]
    [InlineData(typeof(MockAlternativeDecisionStrategy))]
    public void Assemblies_NoNamespaceContainsV2Segment_Theory(Type anchorType)
    {
        var assembly = anchorType.Assembly;
        var namespaces = assembly.GetTypes()
            .Select(type => type.Namespace)
            .Where(ns => ns is not null)
            .Select(ns => ns!)
            .Distinct()
            .ToList();

        namespaces.Should().NotBeEmpty($"assembly '{assembly.GetName().Name}' phải có kiểu để kiểm tra");

        var violations = namespaces
            .Where(ns => ns.Split('.').Contains(LegacySegment, StringComparer.Ordinal))
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty($"assembly '{assembly.GetName().Name}' không được còn namespace có đoạn '{LegacySegment}'");
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void SourceTree_HasNoV2Folders()
    {
        FoldersNamedV2(RepositoryPaths.Resolve("src")).Should().BeEmpty("cây nguồn src/ không được còn thư mục tên 'V2'");
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void TestTree_HasNoV2Folders()
    {
        FoldersNamedV2(RepositoryPaths.Resolve("tests")).Should().BeEmpty("cây test tests/ không được còn thư mục tên 'V2'");
    }
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Runtime_DoesNotReferenceRemovedV1Types()
    {
        var assemblies = RuntimeLayerProjects.Select(SourceTree.AssemblyOf).ToList();

        var removedTypes = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => RemovedV1TypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        removedTypes.Should().BeEmpty("các kiểu dịch vụ v1 đã bị gỡ không được xuất hiện lại");

        assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name)
            .Should().NotContain("MediatR", "runtime không dùng MediatR (dịch vụ gọi trực tiếp)");
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void RuntimeCore_DoesNotReferenceEfPersistenceTypesOrMediatR()
    {
        var coreTypes = RuntimeLayerProjects
            .SelectMany(project => SourceTree.AssemblyOf(project).GetTypes())
            .Where(type => type.Namespace is not { } ns || !TypeReferenceScanner.IsInNamespace(ns, PersistenceNamespace))
            .ToList();

        coreTypes.Should().NotBeEmpty("test không được pass rỗng khi không tìm thấy kiểu nào ngoài Persistence");

        var violations = coreTypes
            .SelectMany(type => TypeReferenceScanner.ReferencedTypes(type)
                .Where(IsForbiddenOutsidePersistence)
                .Select(referenced => $"{type.FullName} -> {referenced.FullName}"))
            .Distinct()
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "mọi namespace của Application, Infrastructure và Runtime.Api ngoài {0} chỉ dùng persistence qua interface, "
            + "không dùng TadssDbContext/entity/EF Core/MediatR; vi phạm: {1}",
            PersistenceNamespace, string.Join("; ", violations));
    }

    private static IEnumerable<string> FoldersNamedV2(string root)
    {
        return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(root, path))
            .Where(path => string.Equals(Path.GetFileName(path), LegacySegment, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsBuildOutput(string root, string path)
    {
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part is "bin" or "obj" or "TestResults");
    }

    private static bool IsForbiddenOutsidePersistence(Type referenced)
    {
        var ns = TypeReferenceScanner.Definition(referenced).Namespace;
        if (ns is null)
        {
            return false;
        }

        return TypeReferenceScanner.IsInNamespace(ns, PersistenceNamespace)
            || TypeReferenceScanner.IsInNamespace(ns, "Microsoft.EntityFrameworkCore")
            || TypeReferenceScanner.IsInNamespace(ns, "MediatR");
    }
}
