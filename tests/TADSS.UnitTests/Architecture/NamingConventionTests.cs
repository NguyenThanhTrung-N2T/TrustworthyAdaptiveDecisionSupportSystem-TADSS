using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// UT-14: quy ước đặt tên và đặt chỗ của mã nguồn <c>src/</c> — namespace khớp project + thư mục, không còn chữ "V2",
/// options/validator nằm trong thư mục <c>Options</c>, exception của runtime nằm trong <c>TADSS.Application.Exceptions</c>.
/// Quét động bằng nguồn và reflection; chỉ allowlist ngoại lệ có lý do. Options của strategy ở gốc project strategy (spec 4.2.2)
/// và exception của SDK nằm trong SDK không cần allowlist vì test options/exception chỉ quét ba project runtime.
/// </summary>
public class NamingConventionTests
{
    private const string LegacyMarker = "V2";
    private const string ExceptionsNamespace = "TADSS.Application.Exceptions";

    /// <summary>File không khai báo namespace (khóa là đường dẫn hiển thị) kèm lý do.</summary>
    private static readonly Dictionary<string, string> FilesWithoutNamespace = new(StringComparer.Ordinal)
    {
        ["TADSS.Runtime.Api/Program.cs"] = "Program.cs dùng top-level statements, chỉ khai báo lớp partial Program ở namespace global để WebApplicationFactory dùng",
    };

    /// <summary>Kiểu được phép mang chữ "V2" trong tên (khóa là full name) kèm lý do.</summary>
    private static readonly Dictionary<string, string> V2NameAllowList = new(StringComparer.Ordinal)
    {
        ["TADSS.Infrastructure.Persistence.Migrations.DecisionPersistenceV2"] =
            "Class migration: tên gắn với ID migration đã áp lên DB (20261002101457_DecisionPersistenceV2), không đổi được",
    };

    public static TheoryData<string> AllProjects => ToTheoryData(SourceTree.Projects);

    public static TheoryData<string> RuntimeProjects => ToTheoryData(SourceTree.RuntimeProjects);

    /// <summary>Namespace khai báo trong mỗi file phải bằng tên project cộng đường dẫn thư mục của file.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AllProjects))]
    public void Namespace_MatchesProjectAndFolder_Theory(string project)
    {
        var files = SourceTree.FilesOf(project);
        files.Should().NotBeEmpty($"project '{project}' phải có file nguồn để kiểm tra");

        var violations = files
            .Select(file => (File: file, Declared: file.DeclaredNamespace))
            .Where(pair => pair.Declared is not null
                ? pair.Declared != pair.File.ExpectedNamespace
                : !FilesWithoutNamespace.ContainsKey(pair.File.Display))
            .Select(pair => $"{pair.File.Display}: khai báo '{pair.Declared ?? "(không có)"}', mong đợi '{pair.File.ExpectedNamespace}'")
            .ToList();

        violations.Should().BeEmpty("namespace phải bằng tên project + đường dẫn thư mục; vi phạm:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, violations));
    }

    /// <summary>Không kiểu nào (kể cả lồng nhau) và không namespace nào chứa "V2", trừ class migration có tên gắn với ID.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(AllProjects))]
    public void Types_HaveNoV2InNameOrNamespace_Theory(string project)
    {
        var types = SourceTree.AssemblyOf(project).GetTypes();
        types.Should().NotBeEmpty($"assembly '{project}' phải có kiểu để kiểm tra");

        var violations = types
            .Where(type => type.FullName is not { } fullName || !V2NameAllowList.ContainsKey(fullName))
            .Where(type => type.Name.Contains(LegacyMarker, StringComparison.Ordinal)
                || (type.Namespace?.Contains(LegacyMarker, StringComparison.Ordinal) ?? false))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("không kiểu/namespace nào được mang '{0}' (ngoại lệ: {1}); vi phạm: {2}",
            LegacyMarker, string.Join(", ", V2NameAllowList.Keys), string.Join(", ", violations));
    }

    /// <summary>Kiểu tên <c>*Options</c> hoặc <c>*OptionsValidator</c> của ba project runtime phải ở namespace kết thúc <c>.Options</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(RuntimeProjects))]
    public void OptionsTypes_LiveInOptionsFolder_Theory(string project)
    {
        var optionsTypes = SourceTree.AssemblyOf(project).GetTypes()
            .Where(type => !type.IsNested)
            .Where(type => type.Name.EndsWith("Options", StringComparison.Ordinal)
                || type.Name.EndsWith("OptionsValidator", StringComparison.Ordinal))
            .ToList();

        optionsTypes.Should().NotBeEmpty($"project '{project}' phải có kiểu options để kiểm tra");

        var violations = optionsTypes
            .Where(type => type.Namespace is not { } ns || !ns.EndsWith(".Options", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("options/validator phải nằm trong namespace kết thúc '.Options'; vi phạm: {0}",
            string.Join(", ", violations));
    }

    /// <summary>Mọi kiểu kế thừa <see cref="Exception"/> trong Application, Infrastructure, Runtime.Api chỉ được ở <c>TADSS.Application.Exceptions</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ExceptionTypes_LiveInApplicationExceptions()
    {
        var exceptionTypes = SourceTree.RuntimeProjects
            .SelectMany(project => SourceTree.AssemblyOf(project).GetTypes())
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .ToList();

        exceptionTypes.Should().NotBeEmpty("Application phải có exception nghiệp vụ để kiểm tra");

        var violations = exceptionTypes
            .Where(type => type.Namespace != ExceptionsNamespace)
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("exception của runtime chỉ được khai báo trong {0}; vi phạm: {1}",
            ExceptionsNamespace, string.Join(", ", violations));
    }

    private static TheoryData<string> ToTheoryData(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values)
        {
            data.Add(value);
        }

        return data;
    }
}
