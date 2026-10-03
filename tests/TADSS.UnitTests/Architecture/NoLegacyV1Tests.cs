using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Runtime.Api.Composition;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-09: không còn dấu vết v1 / cấu trúc tạm <c>V2</c> sau khi chuyển hẳn sang contract v2 — không namespace nào có đoạn
/// <c>V2</c>, không thư mục nguồn tên <c>V2</c>, không còn kiểu dịch vụ v1 đã gỡ, và lõi runtime không phụ thuộc
/// persistence EF hay MediatR (lõi chỉ nói chuyện với hạ tầng qua interface như <c>IDecisionEvaluationStore</c>).
/// </summary>
public class NoLegacyV1Tests
{
    private const string LegacySegment = "V2";
    private const string RuntimeRoot = "TADSS.Runtime.Api";

    /// <summary>Tên kiểu dịch vụ v1 đã bị gỡ ở 5.2c — không được xuất hiện lại trong assembly Runtime.</summary>
    private static readonly string[] RemovedV1TypeNames =
        ["IStrategyResolver", "IDecisionResiliencePipeline", "IDecisionCacheService"];

    /// <summary>Namespace của lõi runtime (không gồm hạ tầng dữ liệu EF, nơi được phép dùng <c>TadssDbContext</c>).</summary>
    private static readonly string[] RuntimeCoreNamespaces =
    [
        RuntimeRoot + ".Services",
        RuntimeRoot + ".Infrastructure.Caching",
        RuntimeRoot + ".Infrastructure.Resilience",
        RuntimeRoot + ".Middlewares",
    ];

    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(typeof(Reservation))]
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
        var assembly = typeof(PresentationServiceCollectionExtensions).Assembly;

        var removedTypes = assembly.GetTypes()
            .Where(type => RemovedV1TypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        removedTypes.Should().BeEmpty("các kiểu dịch vụ v1 đã bị gỡ không được xuất hiện lại");

        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().NotContain("MediatR", "runtime không dùng MediatR (dịch vụ gọi trực tiếp)");
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void RuntimeCore_DoesNotReferenceEfPersistenceTypesOrMediatR()
    {
        var coreTypes = typeof(PresentationServiceCollectionExtensions).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns
                && RuntimeCoreNamespaces.Any(root => ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal)))
            .ToList();

        coreTypes.Should().NotBeEmpty("test không được pass rỗng khi không tìm thấy kiểu lõi nào");

        var violations = coreTypes
            .SelectMany(type => ReferencedTypes(type)
                .Where(IsForbiddenForCore)
                .Select(referenced => $"{type.FullName} -> {referenced.FullName}"))
            .Distinct()
            .ToList();

        violations.Should().BeEmpty("lõi runtime chỉ dùng persistence qua interface, không dùng TadssDbContext/entity EF/MediatR");
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

    private static bool IsForbiddenForCore(Type referenced)
    {
        var definition = referenced.IsGenericType ? referenced.GetGenericTypeDefinition() : referenced;
        var ns = definition.Namespace;
        if (ns is null)
        {
            return false;
        }

        return definition.Name == "TadssDbContext"
            || IsInNamespace(ns, RuntimeRoot + ".Infrastructure.Data.Entities")
            || IsInNamespace(ns, "MediatR");
    }

    private static bool IsInNamespace(string ns, string root)
    {
        return ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(all);
        var direct = type.GetProperties(all).Select(property => property.PropertyType)
            .Concat(type.GetFields(all).Select(field => field.FieldType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(methods.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetConstructors(all).SelectMany(ctor => ctor.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetInterfaces());

        if (type.BaseType is { } baseType)
        {
            direct = direct.Append(baseType);
        }

        return direct.SelectMany(Flatten);
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        var element = type.HasElementType ? type.GetElementType() : null;
        if (element is not null)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
