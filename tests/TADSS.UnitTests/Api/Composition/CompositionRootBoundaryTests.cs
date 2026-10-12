using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14: chỉ composition root (<c>Program.cs</c> và thư mục <c>Composition/</c>) được dùng kiểu hạ tầng; controller, middleware,
/// error handling và OpenAPI của Runtime.Api chỉ thấy Application và SDK (spec 4.2.4). Ba lớp kiểm: văn bản nguồn (<c>using</c>/tên đầy đủ),
/// cửa hậu <c>global using</c> / <c>&lt;Using Include&gt;</c> làm kiểu hạ tầng hiện ra ở mọi file mà không có tham chiếu văn bản,
/// và reflection trên chữ ký/trường của kiểu ngoài composition root.
/// </summary>
public class CompositionRootBoundaryTests
{
    private const string CompositionNamespace = "TADSS.Runtime.Api.Composition";

    /// <summary>Namespace hạ tầng bị cấm ngoài composition root (cũng dùng làm tiền tố tên assembly khi quét reflection).</summary>
    private static readonly string[] InfrastructureNamespaces =
    [
        "TADSS.Infrastructure",
        "Npgsql",
        "Polly",
        "StackExchange.Redis",
        "Microsoft.EntityFrameworkCore",
        // Telemetry (O-02): SDK OpenTelemetry và sink log OTLP chỉ được dựng ở composition root
        "OpenTelemetry",
        "Serilog.Sinks.OpenTelemetry",
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

    /// <summary>
    /// Không file nguồn nào của Runtime.Api — kể cả <c>Composition/</c> và <c>Program.cs</c> — được khai báo <c>global using</c> tới namespace hạ tầng:
    /// <c>global using</c> làm kiểu hạ tầng nhìn thấy ở mọi controller/middleware mà test văn bản theo từng file không bắt được.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ApiSources_DeclareNoGlobalUsingOfInfrastructure()
    {
        var files = SourceTree.FilesOf(SourceTree.RuntimeApi);

        files.Should().NotBeEmpty("phải quét ít nhất một file Runtime.Api, không được pass rỗng");

        var violations = files
            .Select(file => (File: file, Used: InfrastructureNamespaces.Where(ns => IsGlobalUsing(file.CodeWithoutComments(), ns)).ToList()))
            .Where(pair => pair.Used.Count > 0)
            .Select(pair => $"{pair.File.RelativePath}: global using {string.Join(", ", pair.Used)}")
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("không file nào của Runtime.Api (kể cả Composition/) được global using {0}; vi phạm: {1}",
            string.Join(", ", InfrastructureNamespaces), string.Join("; ", violations));
    }

    /// <summary>
    /// <c>TADSS.Runtime.Api.csproj</c> không được khai báo <c>&lt;Using Include="…"/&gt;</c> (global using ở mức project) tới namespace hạ tầng.
    /// Đọc csproj bằng XML nên không bị lệch vì khoảng trắng hay thứ tự thuộc tính.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ApiProject_DeclaresNoUsingItemOfInfrastructure()
    {
        var csproj = XDocument.Load(RepositoryPaths.Resolve("src", SourceTree.RuntimeApi, $"{SourceTree.RuntimeApi}.csproj"));

        csproj.Descendants("ProjectReference").Should().NotBeEmpty("csproj phải đọc được (có ProjectReference), không được pass rỗng");

        var violations = csproj.Descendants("Using")
            .Select(item => (string?)item.Attribute("Include") ?? (string?)item.Attribute("Update"))
            .OfType<string>()
            .Where(include => InfrastructureNamespaces.Any(ns => TypeReferenceScanner.IsInNamespace(include, ns)))
            .OrderBy(include => include, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("csproj của Runtime.Api không được <Using Include> tới {0}; vi phạm: {1}",
            string.Join(", ", InfrastructureNamespaces), string.Join(", ", violations));
    }

    /// <summary>
    /// Kiểu của Runtime.Api ngoài namespace <c>TADSS.Runtime.Api.Composition</c> (và <c>Program</c>) không được tham chiếu kiểu thuộc assembly/namespace hạ tầng
    /// trong trường, property, tham số/kiểu trả về của method và constructor, interface hay kiểu cơ sở. Bổ sung cho test văn bản:
    /// bắt cả trường hợp kiểu hạ tầng lọt vào qua <c>global using</c> hoặc <c>var</c>/kiểu suy luận ở chữ ký. Không thấy biến cục bộ trong thân method (đó là việc của test văn bản).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void ApiTypesOutsideComposition_ReferenceNoInfrastructureTypes()
    {
        var scanned = SourceTree.AssemblyOf(SourceTree.RuntimeApi).GetTypes()
            .Where(type => !IsCompositionRootType(type))
            .ToList();

        scanned.Should().NotBeEmpty("phải quét ít nhất một kiểu Runtime.Api ngoài composition root, không được pass rỗng");

        var referenced = scanned
            .SelectMany(type => TypeReferenceScanner.ReferencedTypes(type).Select(reference => (Type: type, Reference: TypeReferenceScanner.Definition(reference))))
            .ToList();

        referenced.Should().NotBeEmpty("các kiểu được quét phải có tham chiếu để kiểm tra, không được pass rỗng");

        var violations = referenced
            .Where(pair => IsInfrastructureType(pair.Reference))
            .Select(pair => $"{pair.Type.FullName} -> {pair.Reference.FullName}")
            .Distinct()
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty("chỉ Program và {0} được tham chiếu kiểu hạ tầng ({1}); vi phạm: {2}",
            CompositionNamespace, string.Join(", ", InfrastructureNamespaces), string.Join("; ", violations));
    }

    private static bool IsCompositionRoot(SourceFile file)
    {
        return file.RelativePath == "Program.cs" || file.Folders.FirstOrDefault() == "Composition";
    }

    /// <summary>Kiểu thuộc composition root: namespace <c>TADSS.Runtime.Api.Composition</c> hoặc <c>Program</c> (top-level statements, namespace toàn cục).</summary>
    private static bool IsCompositionRootType(Type type)
    {
        return TypeReferenceScanner.IsInNamespace(type.Namespace, CompositionNamespace)
            || (type.Namespace is null && type.Name is "Program" or "<Program>$");
    }

    /// <summary>Kiểu (hoặc assembly chứa nó) thuộc một trong các namespace/assembly hạ tầng bị cấm.</summary>
    private static bool IsInfrastructureType(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name;
        return InfrastructureNamespaces.Any(root =>
            TypeReferenceScanner.IsInNamespace(type.Namespace, root) || TypeReferenceScanner.IsInNamespace(assemblyName, root));
    }

    /// <summary>Có dòng <c>global using [static] [alias =] ns...</c> trong mã đã bỏ comment.</summary>
    private static bool IsGlobalUsing(string code, string ns)
    {
        return Regex.IsMatch(code, $@"^\s*global\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?:global::)?{Regex.Escape(ns)}\b", RegexOptions.Multiline);
    }

    /// <summary>Có dòng <c>using [static] [alias =] ns...</c> hoặc tên đầy đủ <c>ns.X</c> trong mã.</summary>
    private static bool Uses(string code, string ns)
    {
        var escaped = Regex.Escape(ns);
        return Regex.IsMatch(code, $@"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?{escaped}\b", RegexOptions.Multiline)
            || Regex.IsMatch(code, $@"(?<![\w.]){escaped}\.\w");
    }
}
