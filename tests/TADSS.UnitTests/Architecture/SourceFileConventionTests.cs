using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14: quy ước file nguồn của <c>src/</c> — mỗi file tối đa một kiểu public cấp cao nhất và tên file trùng tên kiểu,
/// mỗi file không quá 250 dòng. Mã sinh tự động (<c>Migrations/</c>, <c>*.Designer.cs</c>) được miễn cả hai quy tắc
/// (class migration <c>DecisionPersistenceV2</c> nằm trong <c>Migrations/</c> nên cũng được miễn).
/// </summary>
public class SourceFileConventionTests
{
    /// <summary>Trần số dòng của một file <c>.cs</c> trong <c>src/</c>.</summary>
    private const int MaxLines = 250;

    public static TheoryData<string> AllProjects
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var project in SourceTree.Projects)
            {
                data.Add(project);
            }

            return data;
        }
    }

    /// <summary>Mỗi file có tối đa một kiểu public cấp cao nhất và (khi có) tên file bằng tên kiểu đó.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AllProjects))]
    public void SourceFiles_DeclareOnePublicTopLevelType_Theory(string project)
    {
        var files = SourceTree.FilesOf(project);
        files.Should().NotBeEmpty($"project '{project}' phải có file nguồn để kiểm tra");

        var violations = files
            .Where(file => !file.IsGenerated)
            .Select(file => (File: file, Types: file.PublicTopLevelTypeNames()))
            .Select(pair => pair.Types.Length > 1
                ? $"{pair.File.Display}: {pair.Types.Length} kiểu public ({string.Join(", ", pair.Types)})"
                : pair.Types.Length == 1 && pair.Types[0] != pair.File.FileNameWithoutExtension
                    ? $"{pair.File.Display}: kiểu '{pair.Types[0]}' không trùng tên file"
                    : null)
            .OfType<string>()
            .ToList();

        violations.Should().BeEmpty("mỗi file một kiểu public cấp cao nhất, tên file = tên kiểu; vi phạm:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Lưới an toàn cho bộ quét văn bản: regex chỉ nhận kiểu <c>public</c> khai báo ở cột 0 nên kiểu thụt lề (ví dụ trong namespace dạng khối) sẽ lọt qua
    /// kiểm tra "một kiểu public/file". Số kiểu public cấp cao nhất regex tìm thấy phải bằng số kiểu public không lồng nhau của assembly (reflection);
    /// ngoại lệ khai báo giống test trên: mã sinh (<c>Migrations/</c>, <c>*.Designer.cs</c>) bị bỏ ở cả hai phía.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AllProjects))]
    public void SourceFiles_RegexPublicTypeCount_EqualsReflectionCount_Theory(string project)
    {
        var files = SourceTree.FilesOf(project);
        files.Should().NotBeEmpty($"project '{project}' phải có file nguồn để kiểm tra");

        var scanned = files
            .Where(file => !file.IsGenerated)
            .SelectMany(file => file.PublicTopLevelTypeNames())
            .ToList();

        var reflected = SourceTree.AssemblyOf(project).GetTypes()
            .Where(type => type is { IsPublic: true, IsNested: false })
            .Where(type => type.Namespace?.Split('.').Contains("Migrations", StringComparer.Ordinal) != true)
            .Select(type => type.Name.Split('`')[0])
            .ToList();

        reflected.Should().NotBeEmpty($"assembly '{project}' phải có kiểu public để so khớp");

        var missedByRegex = reflected.Except(scanned, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var missedByReflection = scanned.Except(reflected, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();

        scanned.Count.Should().Be(reflected.Count,
            "regex quét {0} kiểu public cấp cao nhất nhưng reflection thấy {1} trong '{2}' (kiểu thụt lề/namespace khối làm bộ quét văn bản bỏ sót?). "
            + "Chỉ có ở reflection: [{3}]; chỉ có ở regex: [{4}]",
            scanned.Count, reflected.Count, project, string.Join(", ", missedByRegex), string.Join(", ", missedByReflection));
    }

    /// <summary>Không file nguồn nào (trừ <c>Migrations/</c> và <c>*.Designer.cs</c>) vượt 250 dòng.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AllProjects))]
    public void SourceFiles_AreWithinSizeCap_Theory(string project)
    {
        var files = SourceTree.FilesOf(project);
        files.Should().NotBeEmpty($"project '{project}' phải có file nguồn để kiểm tra");

        var violations = files
            .Where(file => !file.IsGenerated)
            .Select(file => (File: file, Lines: file.LineCount))
            .Where(pair => pair.Lines > MaxLines)
            .Select(pair => $"{pair.File.Display}: {pair.Lines} dòng")
            .ToList();

        violations.Should().BeEmpty("mỗi file .cs trong src/ tối đa {0} dòng; vi phạm:{1}{2}",
            MaxLines, Environment.NewLine, string.Join(Environment.NewLine, violations));
    }
}
