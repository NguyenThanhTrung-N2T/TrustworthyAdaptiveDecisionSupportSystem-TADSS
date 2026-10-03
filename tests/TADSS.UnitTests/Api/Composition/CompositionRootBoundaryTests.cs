using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14: chỉ composition root (<c>Program.cs</c> và thư mục <c>Composition/</c>) được dùng kiểu hạ tầng; controller, middleware,
/// error handling và OpenAPI của Runtime.Api chỉ thấy Application và SDK (spec 4.2.4).
/// </summary>
public class CompositionRootBoundaryTests
{
    /// <summary>Namespace hạ tầng bị cấm ngoài composition root.</summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "TADSS.Infrastructure",
        "Npgsql",
        "Polly",
        "StackExchange.Redis",
        "Microsoft.EntityFrameworkCore",
    ];

    /// <summary>
    /// Nguồn Runtime.Api ngoài <c>Program.cs</c> và <c>Composition/</c> không được dùng <c>TADSS.Infrastructure</c>, Npgsql, Polly,
    /// StackExchange.Redis hay Microsoft.EntityFrameworkCore. Khớp theo dòng <c>using</c> hoặc tên đầy đủ trong mã đã bỏ comment
    /// (không khớp chuỗi trần, nên comment nhắc tới Npgsql/Polly không làm test đỏ). Đây cũng là test UT-14
    /// <c>ApiOutsideCompositionRoot_UsesNoInfrastructureTypes</c> của plan 5.2d (mở rộng test cũ của Task 5).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ApiOutsideCompositionRoot_UsesNoInfrastructureTypes()
    {
        var scanned = SourceTree.FilesOf(SourceTree.RuntimeApi)
            .Where(file => !IsCompositionRoot(file))
            .ToList();

        scanned.Should().NotBeEmpty("phải quét ít nhất một file Runtime.Api ngoài composition root, không được pass rỗng");

        var violations = scanned
            .Select(file => (File: file, Used: InfrastructureNamespaces.Where(ns => Uses(file.CodeWithoutComments(), ns)).ToList()))
            .Where(pair => pair.Used.Count > 0)
            .Select(pair => $"{pair.File.RelativePath}: {string.Join(", ", pair.Used)}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("chỉ Program.cs và Composition/ được dùng {0}; vi phạm: {1}",
            string.Join(", ", InfrastructureNamespaces), string.Join("; ", violations));
    }

    private static bool IsCompositionRoot(SourceFile file)
    {
        return file.RelativePath == "Program.cs" || file.Folders.FirstOrDefault() == "Composition";
    }

    /// <summary>Có dòng <c>using [static] [alias =] ns...</c> hoặc tên đầy đủ <c>ns.X</c> trong mã.</summary>
    private static bool Uses(string code, string ns)
    {
        var escaped = Regex.Escape(ns);
        return Regex.IsMatch(code, $@"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?{escaped}\b", RegexOptions.Multiline)
            || Regex.IsMatch(code, $@"(?<![\w.]){escaped}\.\w");
    }
}
