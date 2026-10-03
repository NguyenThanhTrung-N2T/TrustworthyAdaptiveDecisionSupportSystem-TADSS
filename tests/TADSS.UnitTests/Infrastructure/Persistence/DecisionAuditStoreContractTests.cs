using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Infrastructure.Persistence.Entities;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Persistence;

/// <summary>
/// UT-01 (INV-4): khóa bằng phản chiếu tuyên bố "kho audit append-only và không lộ hạ tầng EF" của hai port
/// <see cref="IDecisionEvaluationStore"/> và <see cref="IDecisionQueryStore"/> — không có method sửa/xóa/thêm kiểu chung, và không
/// method nào nhận hay trả <c>IQueryable</c>, <see cref="DbContext"/> hoặc entity EF.
/// </summary>
public class DecisionAuditStoreContractTests
{
    private static readonly string[] ForbiddenNamePrefixes = ["Update", "Delete", "Remove", "Add", "Save", "Attach", "Upsert", "Truncate"];

    private static readonly Type[] StoreInterfaces = [typeof(IDecisionEvaluationStore), typeof(IDecisionQueryStore)];

    private static readonly MethodInfo[] StoreMethods = StoreInterfaces
        .SelectMany(MethodsOf)
        .ToArray();

    public static TheoryData<Type> AuditStoreInterfaces => new() { typeof(IDecisionEvaluationStore), typeof(IDecisionQueryStore) };

    private static IEnumerable<MethodInfo> MethodsOf(Type storeInterface) =>
        storeInterface.GetMethods(BindingFlags.Public | BindingFlags.Instance);

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

    /// <summary>Từng port audit: không có method sửa/xóa/thêm kiểu chung, không nhận hay trả <c>IQueryable</c>/<see cref="DbContext"/>/entity EF.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AuditStoreInterfaces))]
    public void AuditStoreInterfaces_AreAppendOnly_Theory(Type storeInterface)
    {
        var methods = MethodsOf(storeInterface).ToArray();

        methods.Should().NotBeEmpty();
        methods
            .Where(method => ForbiddenNamePrefixes.Any(prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(method => method.Name)
            .Should().BeEmpty("port audit append-only (INV-4): chỉ đọc và InsertExecutionAsync");
        methods
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .SelectMany(Flatten)
            .Where(IsInfrastructureType)
            .Select(type => type.FullName)
            .Distinct()
            .Should().BeEmpty("DbContext/IQueryable/entity EF không được lọt ra khỏi port");
    }

    /// <summary>ISP (E-07): mỗi port chỉ lộ đúng method của consumer — evaluation gồm tra idempotency và ghi, query chỉ tra chi tiết.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void AuditStoreInterfaces_SplitMethodsByConsumer()
    {
        MethodsOf(typeof(IDecisionEvaluationStore)).Select(method => method.Name)
            .Should().BeEquivalentTo("FindPrimaryByIdempotencyKeyAsync", "InsertExecutionAsync");
        MethodsOf(typeof(IDecisionQueryStore)).Select(method => method.Name)
            .Should().BeEquivalentTo("GetDecisionDetailAsync");
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
