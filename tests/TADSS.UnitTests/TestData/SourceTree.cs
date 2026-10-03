using System.Reflection;
using System.Text.RegularExpressions;

namespace TADSS.UnitTests.TestData;

/// <summary>
/// Một file nguồn <c>.cs</c> trong <c>src/</c>: tên project, đường dẫn tương đối trong project (dấu <c>/</c>),
/// và các phép phân tích văn bản dùng cho test kiến trúc (namespace khai báo, kiểu public cấp cao nhất, mã đã bỏ comment).
/// </summary>
internal sealed partial record SourceFile(string Project, string RelativePath, string FullPath)
{
    private static readonly Regex NamespaceDeclaration = NamespaceRegex();
    private static readonly Regex PublicTypeDeclaration = PublicTypeRegex();
    private static readonly Regex CommentsAndStrings = CommentsAndStringsRegex();

    /// <summary>Đường dẫn tương đối kèm tên project, dùng trong message vi phạm (ví dụ <c>TADSS.Application/Decisions/X.cs</c>).</summary>
    public string Display => $"{Project}/{RelativePath}";

    /// <summary>Các đoạn thư mục của <see cref="RelativePath"/> (không gồm tên file).</summary>
    public string[] Folders => RelativePath.Split('/')[..^1];

    /// <summary>Tên file không có đuôi <c>.cs</c>.</summary>
    public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FullPath);

    /// <summary>File nằm trong thư mục <c>Migrations/</c> hoặc là <c>*.Designer.cs</c> (mã sinh tự động, miễn quy tắc kích thước và một kiểu/file).</summary>
    public bool IsGenerated =>
        Folders.Contains("Migrations", StringComparer.Ordinal)
        || FullPath.EndsWith(".Designer.cs", StringComparison.Ordinal);

    /// <summary>Số dòng của file.</summary>
    public int LineCount => File.ReadAllLines(FullPath).Length;

    /// <summary>Namespace khai báo đầu tiên của file, hoặc <c>null</c> nếu file không khai báo (ví dụ top-level statements).</summary>
    public string? DeclaredNamespace
    {
        get
        {
            var match = NamespaceDeclaration.Match(File.ReadAllText(FullPath));
            return match.Success ? match.Groups["ns"].Value : null;
        }
    }

    /// <summary>Namespace mong đợi: tên project cộng đường dẫn thư mục (spec 4.2.4).</summary>
    public string ExpectedNamespace => string.Join('.', [Project, .. Folders]);

    /// <summary>Tên các kiểu public cấp cao nhất (class, record, struct, interface, enum, delegate) khai báo ở cột 0 của file.</summary>
    public string[] PublicTopLevelTypeNames()
    {
        return PublicTypeDeclaration.Matches(File.ReadAllText(FullPath))
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    /// <summary>Nội dung file đã bỏ comment (giữ nguyên chuỗi ký tự) để quét tên namespace mà không bị comment làm nhiễu.</summary>
    public string CodeWithoutComments()
    {
        return CommentsAndStrings.Replace(
            File.ReadAllText(FullPath),
            match => match.Groups["comment"].Success ? string.Empty : match.Value);
    }

    [GeneratedRegex(@"^namespace\s+(?<ns>[\w.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(
        @"^public\s+(?:(?:sealed|static|abstract|partial|readonly|unsafe|ref|file)\s+)*"
        + @"(?:(?:record\s+struct|record\s+class|record|class|struct|interface|enum)\s+|delegate\s+[^\s(]+\s+)(?<name>[A-Za-z_]\w*)",
        RegexOptions.Multiline)]
    private static partial Regex PublicTypeRegex();

    [GeneratedRegex(@"(?<comment>//[^\r\n]*|/\*.*?\*/)|@""(?:""""|[^""])*""|""(?:\\.|[^""\\\r\n])*""", RegexOptions.Singleline)]
    private static partial Regex CommentsAndStringsRegex();
}

/// <summary>
/// Cây nguồn <c>src/</c> của repo: liệt kê file <c>.cs</c> theo project (bỏ <c>obj/</c>, <c>bin/</c>) và ánh xạ tên project sang assembly,
/// cho test kiến trúc quét động mọi project thay vì dùng danh sách tên cứng.
/// </summary>
internal static class SourceTree
{
    public const string Sdk = "Shared.DecisionContractSDK";
    public const string Application = "TADSS.Application";
    public const string Infrastructure = "TADSS.Infrastructure";
    public const string RuntimeApi = "TADSS.Runtime.Api";
    public const string DefaultStrategy = "TADSS.Strategies.Default";
    public const string MockAlternativeStrategy = "TADSS.Strategies.MockAlternative";

    /// <summary>Tên các project dưới <c>src/</c>.</summary>
    public static readonly string[] Projects =
        [Sdk, Application, Infrastructure, RuntimeApi, DefaultStrategy, MockAlternativeStrategy];

    /// <summary>Ba project lớp Clean Architecture của runtime (Application, Infrastructure, Runtime.Api).</summary>
    public static readonly string[] RuntimeProjects = [Application, Infrastructure, RuntimeApi];

    private static readonly Dictionary<string, Assembly> Assemblies = new()
    {
        [Sdk] = typeof(Shared.DecisionContractSDK.Domain.Reservation).Assembly,
        [Application] = typeof(TADSS.Application.ApplicationServiceCollectionExtensions).Assembly,
        [Infrastructure] = typeof(TADSS.Infrastructure.InfrastructureServiceCollectionExtensions).Assembly,
        [RuntimeApi] = typeof(TADSS.Runtime.Api.Composition.PresentationServiceCollectionExtensions).Assembly,
        [DefaultStrategy] = typeof(TADSS.Strategies.Default.DefaultDecisionStrategy).Assembly,
        [MockAlternativeStrategy] = typeof(TADSS.Strategies.MockAlternative.MockAlternativeDecisionStrategy).Assembly,
    };

    /// <summary>Assembly của một project src.</summary>
    public static Assembly AssemblyOf(string project) => Assemblies[project];

    /// <summary>Mọi file <c>.cs</c> của một project (bỏ <c>obj/</c>, <c>bin/</c>), sắp theo đường dẫn.</summary>
    public static IReadOnlyList<SourceFile> FilesOf(string project)
    {
        var root = RepositoryPaths.Resolve("src", project);
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(path => new SourceFile(project, Path.GetRelativePath(root, path).Replace('\\', '/'), path))
            .Where(file => !file.RelativePath.Split('/').Any(part => part is "bin" or "obj"))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Mọi file <c>.cs</c> của toàn bộ <c>src/</c>.</summary>
    public static IReadOnlyList<SourceFile> AllFiles() => Projects.SelectMany(FilesOf).ToList();
}
