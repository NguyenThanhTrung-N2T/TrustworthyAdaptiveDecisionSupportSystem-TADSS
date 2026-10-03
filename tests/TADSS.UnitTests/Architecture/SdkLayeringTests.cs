using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-09: phân tầng của SDK — Domain/Decisions/Interfaces không phụ thuộc DTO; Common/Enums không phụ thuộc
/// Domain, Decisions hay DTO (bổ sung cho <c>SdkDependencyPurityTests</c>); SDK không phụ thuộc Runtime/Strategies
/// và mỗi Strategy chỉ phụ thuộc SDK trong số các assembly của hệ thống.
/// </summary>
public class SdkLayeringTests
{
    private const string Sdk = "Shared.DecisionContractSDK.";
    private const string SdkAssemblyName = "Shared.DecisionContractSDK";

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void DomainDecisionsAndInterfaces_DoNotReference_Dtos()
    {
        AssertNoReferences(
            sourceNamespaces: [Sdk + "Domain", Sdk + "Decisions", Sdk + "Interfaces"],
            forbiddenPrefixes: [Sdk + "DTOs"]);
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void CommonAndEnums_DoNotReference_DomainDecisionsOrDtos()
    {
        AssertNoReferences(
            sourceNamespaces: [Sdk + "Common", Sdk + "Enums"],
            forbiddenPrefixes: [Sdk + "Domain", Sdk + "Decisions", Sdk + "DTOs"]);
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Sdk_DoesNotReference_RuntimeOrStrategyAssemblies()
    {
        var referenced = typeof(Reservation).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("TADSS.", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEmpty("SDK là contract thuần, không được phụ thuộc Runtime hay Strategies");
    }

    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData(typeof(DefaultDecisionStrategy))]
    [InlineData(typeof(MockAlternativeDecisionStrategy))]
    public void Strategies_ReferenceOnlySdk_AmongSystemAssemblies(Type strategyType)
    {
        var referenced = strategyType.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("TADSS.", StringComparison.Ordinal)
                || name.StartsWith("Shared.", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEquivalentTo([SdkAssemblyName], $"strategy '{strategyType.Name}' chỉ được phụ thuộc SDK");
    }

    private static void AssertNoReferences(string[] sourceNamespaces, string[] forbiddenPrefixes)
    {
        var types = typeof(Reservation).Assembly.GetTypes()
            .Where(type => type.Namespace is not null && sourceNamespaces.Contains(type.Namespace))
            .ToList();

        types.Should().NotBeEmpty();

        foreach (var type in types)
        {
            var violations = ReferencedTypes(type)
                .Where(referenced => referenced.Namespace is { } ns
                    && forbiddenPrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(referenced => referenced.FullName)
                .Distinct()
                .ToList();

            violations.Should().BeEmpty($"'{type.FullName}' không được phụ thuộc {string.Join(", ", forbiddenPrefixes)}");
        }
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(all);
        var direct = type.GetProperties(all).Select(property => property.PropertyType)
            .Concat(type.GetFields(all).Select(field => field.FieldType))
            .Concat(methods.Select(method => method.ReturnType))
            .Concat(methods.SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)))
            .Concat(type.GetConstructors(all).SelectMany(ctor => ctor.GetParameters().Select(parameter => parameter.ParameterType)));

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
