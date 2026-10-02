using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.V2.Architecture;

/// <summary>
/// UT-09: phân tầng của SDK v2 — Domain/Decisions/Interfaces không phụ thuộc DTO; Common/Enums không phụ thuộc
/// Domain, Decisions hay DTO (cùng nguyên tắc với <c>SdkDependencyPurityTests</c> của v1).
/// </summary>
public class SdkV2LayeringTests
{
    private const string V2 = "Shared.DecisionContractSDK.";

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void DomainDecisionsAndInterfaces_DoNotReference_Dtos()
    {
        AssertNoReferences(
            sourceNamespaces: [V2 + "Domain", V2 + "Decisions", V2 + "Interfaces"],
            forbiddenPrefixes: [V2 + "DTOs"]);
    }

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void CommonAndEnums_DoNotReference_DomainDecisionsOrDtos()
    {
        AssertNoReferences(
            sourceNamespaces: [V2 + "Common", V2 + "Enums"],
            forbiddenPrefixes: [V2 + "Domain", V2 + "Decisions", V2 + "DTOs"]);
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
