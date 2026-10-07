using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// Kiến trúc & Ranh giới SDK (Architectural Boundaries & DAG Enforcement Tests).
/// Khẳng định:
/// 1. SDK hoàn toàn thuần BCL (.NET 8), 0 external NuGet dependencies.
/// 2. Cấu trúc DAG phân tầng rõ ràng: Common/Enums -> Domain <- Mappings <- DTOs.
/// 3. Domain Layer KHÔNG phụ thuộc vào DTO Layer.
/// 4. Common & Enums KHÔNG phụ thuộc vào Domain hay DTOs.
/// </summary>
public class SdkDependencyPurityTests
{
    [Fact]
    [Trait("CatalogId", "UT-09")]
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

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void DomainLayer_DoesNotReference_DtoLayer()
    {
        var sdkAssembly = typeof(Reservation).Assembly;
        var domainTypes = sdkAssembly.GetExportedTypes()
            .Where(t => t.Namespace == "Shared.DecisionContractSDK.Domain")
            .ToList();

        domainTypes.Should().NotBeEmpty();

        foreach (var domainType in domainTypes)
        {
            // 1. Check properties
            var propTypes = domainType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => p.PropertyType);

            // 2. Check fields
            var fieldTypes = domainType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => f.FieldType);

            // 3. Check methods parameter & return types
            var methods = domainType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            var methodTypes = methods.Select(m => m.ReturnType)
                .Concat(methods.SelectMany(m => m.GetParameters().Select(p => p.ParameterType)));

            // 4. Check constructors
            var ctors = domainType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var ctorParamTypes = ctors.SelectMany(c => c.GetParameters().Select(p => p.ParameterType));

            var allReferencedTypes = propTypes
                .Concat(fieldTypes)
                .Concat(methodTypes)
                .Concat(ctorParamTypes)
                .Where(t => t.Namespace != null)
                .ToList();

            var dtoReferences = allReferencedTypes
                .Where(t => t.Namespace!.StartsWith("Shared.DecisionContractSDK.DTOs", StringComparison.Ordinal))
                .Select(t => t.FullName)
                .Distinct()
                .ToList();

            dtoReferences.Should().BeEmpty(
                $"Domain type '{domainType.FullName}' must not depend on any DTO type. Found: {string.Join(", ", dtoReferences)}");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void EnumsAndCommon_DoNotReference_DomainOrDtoLayers()
    {
        var sdkAssembly = typeof(Reservation).Assembly;
        var primitiveTypes = sdkAssembly.GetExportedTypes()
            .Where(t => t.Namespace == "Shared.DecisionContractSDK.Enums" || t.Namespace == "Shared.DecisionContractSDK.Common")
            .ToList();

        primitiveTypes.Should().NotBeEmpty();

        foreach (var type in primitiveTypes)
        {
            var propTypes = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(p => p.PropertyType);
            var fieldTypes = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(f => f.FieldType);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            var methodTypes = methods.Select(m => m.ReturnType)
                .Concat(methods.SelectMany(m => m.GetParameters().Select(p => p.ParameterType)));

            var allReferencedTypes = propTypes
                .Concat(fieldTypes)
                .Concat(methodTypes)
                .Where(t => t.Namespace != null)
                .ToList();

            var domainOrDtoReferences = allReferencedTypes
                .Where(t => t.Namespace!.StartsWith("Shared.DecisionContractSDK.Domain", StringComparison.Ordinal)
                    || t.Namespace!.StartsWith("Shared.DecisionContractSDK.DTOs", StringComparison.Ordinal))
                .Select(t => t.FullName)
                .Distinct()
                .ToList();

            domainOrDtoReferences.Should().BeEmpty(
                $"Enums/Common type '{type.FullName}' must not depend on Domain or DTO layers. Found: {string.Join(", ", domainOrDtoReferences)}");
        }
    }
}
