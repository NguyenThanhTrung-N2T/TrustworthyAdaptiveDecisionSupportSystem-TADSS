using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14: tầng Application không tự đọc hay theo dõi cấu hình lúc chạy. Đọc file cấu hình và theo dõi thay đổi là việc của hạ tầng
/// (adapter của port <c>TADSS.Application.Abstractions.Configuration.IRoutingRulesSource</c>); Application chỉ nhận options đã bind
/// hoặc đi qua port. Vì vậy không kiểu nào trong assembly <c>TADSS.Application</c> (kể cả kiểu lồng, kiểu do compiler sinh cho closure)
/// được có trường, property hay tham số constructor mà kiểu là <see cref="IConfiguration"/> hoặc kiểu dẫn xuất
/// (<see cref="IConfigurationSection"/>, <see cref="IConfigurationRoot"/>, <c>ConfigurationManager</c>…), kể cả khi nằm trong đối số generic hay mảng.
/// Hàm <c>static</c> đăng ký DI (<c>AddApplication(IServiceCollection, IConfiguration)</c>) được phép vì chỉ chạy lúc dựng host —
/// test chỉ xét trường, property và constructor nên tham số của method tĩnh tự nhiên không bị bắt.
/// </summary>
public class ApplicationConfigurationBoundaryTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Không kiểu nào của Application giữ <see cref="IConfiguration"/> (trường, property) hay nhận nó qua constructor.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ApplicationTypes_DoNotHoldOrInjectConfiguration()
    {
        var types = SourceTree.AssemblyOf(SourceTree.Application).GetTypes();
        types.Should().NotBeEmpty("test không được pass rỗng khi không nạp được assembly Application");

        var violations = types
            .SelectMany(Members)
            .Where(member => member.Types.Any(IsConfiguration))
            .Select(member => member.Display)
            .Distinct()
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "Application không được tự đọc/theo dõi IConfiguration lúc chạy (dùng options hoặc port ở Application/Abstractions); vi phạm: {0}",
            string.Join("; ", violations));
    }

    /// <summary>Tự kiểm: bộ nhận diện bắt được <see cref="IConfiguration"/> và kiểu dẫn xuất, kể cả trong generic/mảng, và không bắt nhầm kiểu khác.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData(typeof(IConfiguration), true)]
    [InlineData(typeof(IConfigurationSection), true)]
    [InlineData(typeof(IConfigurationRoot), true)]
    [InlineData(typeof(ConfigurationManager), true)]
    [InlineData(typeof(Lazy<IConfigurationSection>), true)]
    [InlineData(typeof(IConfiguration[]), true)]
    [InlineData(typeof(IConfigurationBuilder), false)]
    [InlineData(typeof(string), false)]
    public void IsConfiguration_DetectsConfigurationTypes_Theory(Type type, bool expected)
    {
        IsConfiguration(type).Should().Be(expected);
    }

    private static IEnumerable<(string Display, IEnumerable<Type> Types)> Members(Type type)
    {
        foreach (var field in type.GetFields(AllDeclared))
        {
            yield return ($"{type.FullName}.{field.Name} (trường)", [field.FieldType]);
        }

        foreach (var property in type.GetProperties(AllDeclared))
        {
            yield return ($"{type.FullName}.{property.Name} (property)", [property.PropertyType]);
        }

        foreach (var constructor in type.GetConstructors(AllDeclared))
        {
            yield return ($"{type.FullName} (constructor)", constructor.GetParameters().Select(parameter => parameter.ParameterType));
        }
    }

    private static bool IsConfiguration(Type type)
    {
        if (typeof(IConfiguration).IsAssignableFrom(type))
        {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is { } element && IsConfiguration(element))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsConfiguration);
    }
}
