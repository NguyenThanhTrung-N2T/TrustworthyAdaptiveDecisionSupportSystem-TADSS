using System.Reflection;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using TADSS.Runtime.Api.V2.Configuration;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Architecture;

/// <summary>
/// UT-09: ranh giới v1/v2 của runtime — mã trong <c>TADSS.Runtime.Api.V2</c> chỉ dùng SDK v2 (cùng hai ngoại lệ
/// dùng chung) và không phụ thuộc các kiểu runtime v1 sẽ bị xóa ở gói 5.2c.
/// </summary>
public class RuntimeV2LayeringTests
{
    private const string RuntimeV2Namespace = "TADSS.Runtime.Api.V2";
    private const string SdkRoot = "Shared.DecisionContractSDK.";
    private const string SdkV2Root = "Shared.DecisionContractSDK";

    private static readonly string[] SdkV1Namespaces =
        ["Domain", "DTOs", "Enums", "Interfaces", "Common"];

    private static readonly HashSet<Type> AllowedSharedSdkTypes =
        [typeof(DomainInvariantException), typeof(ContractValidationException)];

    /// <summary>Kiểu runtime v1 còn lại bị cấm (các kiểu dịch vụ/cache/pipeline v1 đã bị xóa ở 5.2c Task 2; EF v1 sẽ thay ở Task 5).</summary>
    private static readonly HashSet<Type> ForbiddenRuntimeV1Types =
    [
        typeof(TadssDbContext), typeof(DecisionExecution),
    ];

    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void RuntimeV2Types_DoNotReferenceSdkV1OrRuntimeV1()
    {
        var v2Types = typeof(ApprovalOptions).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns
                && (ns == RuntimeV2Namespace || ns.StartsWith(RuntimeV2Namespace + ".", StringComparison.Ordinal)))
            .ToList();

        v2Types.Should().NotBeEmpty("test không được pass rỗng khi không tìm thấy kiểu V2 nào");

        var violations = v2Types
            .SelectMany(type => ReferencedTypes(type)
                .Where(IsForbidden)
                .Select(referenced => $"{type.FullName} -> {referenced.FullName}"))
            .Distinct()
            .ToList();

        violations.Should().BeEmpty("runtime v2 chỉ được dùng SDK v2, DomainInvariantException, ContractValidationException và hạ tầng dùng chung");
    }

    private static bool IsForbidden(Type referenced)
    {
        var definition = referenced.IsGenericType ? referenced.GetGenericTypeDefinition() : referenced;
        if (ForbiddenRuntimeV1Types.Contains(definition))
        {
            return true;
        }

        var ns = definition.Namespace;
        if (ns is null)
        {
            return false;
        }

        if (ns == "MediatR" || ns.StartsWith("MediatR.", StringComparison.Ordinal))
        {
            return true;
        }

        return !AllowedSharedSdkTypes.Contains(definition)
            && !ns.StartsWith(SdkV2Root, StringComparison.Ordinal)
            && SdkV1Namespaces.Any(part =>
            {
                var sdkNamespace = SdkRoot + part;
                return ns == sdkNamespace || ns.StartsWith(sdkNamespace + ".", StringComparison.Ordinal);
            });
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
