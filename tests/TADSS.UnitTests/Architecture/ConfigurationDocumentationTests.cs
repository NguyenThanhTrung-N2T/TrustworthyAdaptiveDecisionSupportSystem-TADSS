using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.Runtime.Api.Composition;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// DOC-4: bảng <c>docs/guides/CONFIGURATION_REFERENCE.md</c> khớp hai chiều với cấu hình thật: mỗi kiểu có hằng <c>SectionName</c> bắt đầu bằng <c>Tadss:</c>
/// góp các khóa <c>{SectionName}:{thuộc tính public có setter}</c> (setter <c>init</c> cũng tính); tài liệu liệt kê mỗi khóa đúng một dòng dạng <c>| `Tadss:...` |</c>.
/// Options của hai strategy không có hằng <c>SectionName</c> (runtime bind theo <see cref="StrategyRegistrations"/>) nên được khai báo tường minh ở <see cref="StrategySections"/>.
/// Thuộc tính kiểu phức hợp (danh sách, đối tượng lồng) là một khóa; khóa con được mô tả trong cột ý nghĩa, không thành dòng riêng.
/// </summary>
public class ConfigurationDocumentationTests
{
    private static readonly Regex DocumentedKey = new(@"^\|\s*`(Tadss:[A-Za-z0-9:]+)`\s*\|", RegexOptions.Multiline);

    /// <summary>Section của strategy: runtime bind <c>Tadss:Strategies:{Default|MockAlternative}</c> vào options của project strategy (không có hằng <c>SectionName</c> trên kiểu).</summary>
    private static readonly (string Section, Type OptionsType)[] StrategySections =
    [
        (StrategyRegistrations.DefaultSectionName, typeof(DefaultStrategyOptions)),
        (StrategyRegistrations.MockAlternativeSectionName, typeof(MockAlternativeStrategyOptions)),
    ];

    /// <summary>Không thiếu khóa nào của mã và không có khóa tài liệu đã gỡ khỏi mã; không khóa nào xuất hiện hai lần.</summary>
    [Fact]
    [Trait("CatalogId", "DOC-4")]
    public void ConfigurationReference_MatchesOptionsTypes()
    {
        var text = File.ReadAllText(RepositoryPaths.Resolve("docs", "guides", "CONFIGURATION_REFERENCE.md"));
        var documented = DocumentedKey.Matches(text).Select(match => match.Groups[1].Value).ToList();

        var expected = DiscoverSections()
            .SelectMany(item => item.OptionsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{item.Section}:{property.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        expected.Should().NotBeEmpty("phải tìm thấy options có SectionName, không được pass rỗng");
        documented.Should().OnlyHaveUniqueItems("mỗi khóa đúng một dòng");
        documented.Should().BeEquivalentTo(expected, "bảng cấu hình phải khớp hai chiều với các Options của mã");
    }

    /// <summary>Mọi cặp (section, kiểu options): kiểu có hằng <c>SectionName</c> bắt đầu bằng <c>Tadss:</c> ở mọi project src, cộng section của hai strategy.</summary>
    private static List<(string Section, Type OptionsType)> DiscoverSections()
    {
        var sections = new List<(string Section, Type OptionsType)>();
        foreach (var type in SourceTree.Projects.SelectMany(project => SourceTree.AssemblyOf(project).GetTypes()))
        {
            if (type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is string section
                && section.StartsWith("Tadss:", StringComparison.Ordinal))
            {
                sections.Add((section, type));
            }
        }
        sections.AddRange(StrategySections);
        return sections;
    }
}
