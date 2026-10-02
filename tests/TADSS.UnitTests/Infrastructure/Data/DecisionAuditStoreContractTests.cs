using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Data;

/// <summary>
/// UT-01 (INV-4): khóa bằng phản chiếu tuyên bố "kho audit append-only và không lộ hạ tầng EF" của
/// <see cref="IDecisionAuditStore"/> — không có method sửa/xóa/thêm kiểu chung, và không method nào nhận hay trả
/// <c>IQueryable</c>, <see cref="DbContext"/> hoặc entity EF.
/// </summary>
public class DecisionAuditStoreContractTests
{
    private static readonly string[] ForbiddenNamePrefixes = ["Update", "Delete", "Remove", "Add", "Save", "Attach", "Upsert", "Truncate"];

    private static readonly MethodInfo[] StoreMethods = typeof(IDecisionAuditStore)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .ToArray();

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Interface_ExposesOnlyTheThreeExpectedMethods()
    {
        StoreMethods.Select(method => method.Name).Should().BeEquivalentTo(
            "FindPrimaryByIdempotencyKeyAsync",
            "InsertExecutionAsync",
            "GetDecisionDetailAsync");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Interface_HasNoUpdateDeleteRemoveOrGenericAddMethods()
    {
        var offenders = StoreMethods
            .Where(method => ForbiddenNamePrefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(method => method.Name);

        offenders.Should().BeEmpty("kho audit append-only (INV-4): chỉ đọc và InsertExecutionAsync");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Interface_DoesNotLeakQueryableDbContextOrEntityTypes()
    {
        var leaked = StoreMethods
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .SelectMany(Flatten)
            .Where(IsInfrastructureType)
            .Select(type => type.FullName)
            .Distinct();

        leaked.Should().BeEmpty("DbContext/IQueryable/entity EF không được lọt ra khỏi store");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Entities_AreNotReachableFromTheReadModelRecords()
    {
        var recordTypes = new[] { typeof(DecisionExecutionRecord), typeof(DecisionOverrideRecord), typeof(DecisionOutcomeRecord), typeof(DecisionDetailRecord), typeof(InsertExecutionResult) };

        var leaked = recordTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(property => Flatten(property.PropertyType))
            .Where(IsInfrastructureType)
            .Select(type => type.FullName)
            .Distinct();

        leaked.Should().BeEmpty();
    }

    /// <summary>Trải phẳng kiểu generic (<c>Task&lt;T&gt;</c>, <c>Nullable&lt;T&gt;</c>, mảng…) thành mọi kiểu thành phần.</summary>
    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element)
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

    private static bool IsInfrastructureType(Type type) =>
        typeof(IQueryable).IsAssignableFrom(type)
        || typeof(DbContext).IsAssignableFrom(type)
        || type.Namespace == typeof(DecisionExecution).Namespace;
}
