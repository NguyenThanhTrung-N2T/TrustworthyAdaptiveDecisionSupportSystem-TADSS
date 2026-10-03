using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TADSS.Application.Decisions;
using TADSS.Infrastructure.Persistence;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14: tỷ lệ cọc luôn có đúng 4 chữ số thập phân ở mọi tầng — hằng <c>MaxRateDecimals</c> của hai options strategy và của
/// <c>DecisionResponseComposer</c>, và scale của cột <c>deposit_rate</c> trong model EF (C-09, DECISION_PERSISTENCE_INVARIANTS).
/// Đổi một nơi mà không đổi nơi kia sẽ làm response/DB lệch nhau, nên test khóa cả bốn.
/// </summary>
public class RateScaleInvariantTests
{
    private const string ConstantName = "MaxRateDecimals";
    private const string RateColumn = "deposit_rate";
    private const int ExpectedDecimals = 4;

    /// <summary>Kiểu bắt buộc phải khai báo hằng <c>MaxRateDecimals</c> (để hằng bị xóa/đổi tên thì test vẫn đỏ chứ không pass rỗng).</summary>
    private static readonly Type[] RequiredOwners =
        [typeof(DefaultStrategyOptions), typeof(MockAlternativeStrategyOptions), typeof(DecisionResponseComposer)];

    /// <summary>Hằng <c>MaxRateDecimals</c> (kể cả private) của mọi kiểu src đều bằng 4, có đủ ba chủ sở hữu, và scale cột <c>deposit_rate</c> cũng bằng 4.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void RateDecimals_AreFourEverywhere()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var constants = SourceTree.Projects
            .SelectMany(project => SourceTree.AssemblyOf(project).GetTypes())
            .SelectMany(type => type.GetFields(all)
                .Where(field => field.Name == ConstantName && field.IsLiteral)
                .Select(field => (Owner: type, Value: field.GetRawConstantValue())))
            .ToList();

        constants.Select(constant => constant.Owner).Should().Contain(RequiredOwners,
            $"cả {RequiredOwners.Length} nơi giữ giới hạn thập phân của tỷ lệ cọc phải còn khai báo '{ConstantName}'");

        var wrongConstants = constants
            .Where(constant => !Equals(constant.Value, ExpectedDecimals))
            .Select(constant => $"{constant.Owner.FullName}.{ConstantName} = {constant.Value}")
            .ToList();

        wrongConstants.Should().BeEmpty("mọi '{0}' phải bằng {1}; vi phạm: {2}",
            ConstantName, ExpectedDecimals, string.Join(", ", wrongConstants));

        var scales = DepositRateScales();
        scales.Should().NotBeEmpty($"model EF phải có cột '{RateColumn}'");

        var wrongScales = scales
            .Where(scale => scale.Scale != ExpectedDecimals)
            .Select(scale => $"{scale.Entity}.{RateColumn} scale = {scale.Scale?.ToString() ?? "(không đặt)"}")
            .ToList();

        wrongScales.Should().BeEmpty("scale của cột '{0}' phải bằng {1}; vi phạm: {2}",
            RateColumn, ExpectedDecimals, string.Join(", ", wrongScales));
    }

    private static List<(string Entity, int? Scale)> DepositRateScales()
    {
        var options = new DbContextOptionsBuilder<TadssDbContext>()
            .UseNpgsql("Host=localhost;Database=none")
            .Options;
        using var context = new TadssDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        return model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.GetColumnName() == RateColumn)
                .Select(property => (Entity: entity.ClrType.Name, Scale: property.GetScale())))
            .ToList();
    }
}
