using System.Xml.Linq;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// Khóa quy tắc phụ thuộc giữa các project <c>src/</c> (spec 4.2.4) bằng cách đọc thẳng file csproj.
/// Không dùng reflection vì assembly rỗng hoặc chưa dùng kiểu nào thì không ghi tham chiếu vào metadata,
/// trong khi <c>ProjectReference</c>/<c>PackageReference</c> trong csproj luôn phản ánh đúng ý định kiến trúc.
/// Mỗi project có một allowlist <c>ProjectReference</c>; Application và Infrastructure có thêm allowlist <c>PackageReference</c>
/// và phải dùng <c>Microsoft.NET.Sdk</c> không kèm <c>FrameworkReference</c> (cấm ASP.NET Core ở cả hai tầng).
/// Thứ gì ngoài allowlist đều làm test đỏ.
/// </summary>
public class ProjectReferenceRulesTests
{
    private const string Sdk = "Shared.DecisionContractSDK";
    private const string Application = "TADSS.Application";
    private const string Infrastructure = "TADSS.Infrastructure";
    private const string RuntimeApi = "TADSS.Runtime.Api";
    private const string DefaultStrategy = "TADSS.Strategies.Default";
    private const string MockAlternativeStrategy = "TADSS.Strategies.MockAlternative";

    /// <summary>Allowlist <c>ProjectReference</c> của từng project src (khóa là tên project).</summary>
    private static readonly Dictionary<string, string[]> ProjectReferenceAllowList = new()
    {
        [Sdk] = [],
        [DefaultStrategy] = [Sdk],
        [MockAlternativeStrategy] = [Sdk],
        [Application] = [Sdk],
        [Infrastructure] = [Application, Sdk],
        [RuntimeApi] = [Application, Infrastructure, Sdk, DefaultStrategy, MockAlternativeStrategy]
    };

    /// <summary>
    /// Allowlist <c>PackageReference</c> của Application: chỉ abstraction của nền tảng
    /// (<c>Microsoft.Extensions.*</c>), cấm ASP.NET Core, EF Core, Npgsql, Redis, Polly.
    /// </summary>
    private static readonly string[] ApplicationPackageAllowList =
    [
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Options.ConfigurationExtensions"
    ];

    /// <summary>
    /// Tên gốc <c>PackageReference</c> được phép của Infrastructure (khớp đúng tên hoặc <c>tên + "."</c>): EF Core/Npgsql, Redis, Polly và
    /// <c>Microsoft.Extensions.*</c>; cấm <c>Microsoft.AspNetCore.*</c> và Swashbuckle.
    /// </summary>
    private static readonly string[] InfrastructurePackageAllowedRoots =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "AspNetCore.HealthChecks.NpgSql",
        "StackExchange.Redis",
        "Polly",
        "Microsoft.Extensions"
    ];

    public static TheoryData<string> SrcProjects => new()
    {
        Sdk,
        Application,
        Infrastructure,
        RuntimeApi,
        DefaultStrategy,
        MockAlternativeStrategy
    };

    public static TheoryData<string> ApplicationAndInfrastructure => new()
    {
        Application,
        Infrastructure
    };

    /// <summary>Mỗi project src chỉ được tham chiếu các project nằm trong allowlist của nó.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(SrcProjects))]
    public void ProjectReferences_EachSrcProject_MatchesAllowList_Theory(string projectName)
    {
        var references = ReadProjectReferences(projectName);

        references.Should().BeSubsetOf(
            ProjectReferenceAllowList[projectName],
            $"{projectName} chỉ được tham chiếu project trong allowlist (spec 4.2.4)");
    }

    /// <summary>Application và Infrastructure chỉ được dùng gói NuGet trong allowlist của mình.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(ApplicationAndInfrastructure))]
    public void PackageReferences_ApplicationAndInfrastructure_MatchAllowList_Theory(string projectName)
    {
        var packages = ReadPackageReferences(projectName);

        var violations = projectName == Application
            ? packages.Where(p => !ApplicationPackageAllowList.Contains(p, StringComparer.Ordinal)).ToArray()
            : packages.Where(p => !InfrastructurePackageAllowedRoots
                .Any(root => p.Equals(root, StringComparison.Ordinal) || p.StartsWith(root + ".", StringComparison.Ordinal))).ToArray();

        violations.Should().BeEmpty(
            $"{projectName} chỉ được dùng gói NuGet trong allowlist (Application: không ASP.NET Core/EF/Npgsql/Redis/Polly; "
            + "Infrastructure: không Microsoft.AspNetCore.*/Swashbuckle)");
    }

    /// <summary>
    /// Application và Infrastructure dùng SDK thường <c>Microsoft.NET.Sdk</c> (không phải <c>Microsoft.NET.Sdk.Web</c>) và không khai báo
    /// <c>FrameworkReference</c> nào: shared framework ASP.NET Core chỉ được kéo vào qua Web SDK hoặc <c>FrameworkReference</c>,
    /// nên đây là cổng chặn ASP.NET Core cho cả hai tầng kể cả khi chưa có kiểu nào dùng đến.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [MemberData(nameof(ApplicationAndInfrastructure))]
    public void Csproj_ApplicationAndInfrastructure_UsePlainSdkAndNoFrameworkReference_Theory(string projectName)
    {
        var project = LoadProject(projectName);

        var sdk = (string?)project.Root?.Attribute("Sdk");
        sdk.Should().Be("Microsoft.NET.Sdk", $"{projectName} phải dùng SDK thường, không dùng Microsoft.NET.Sdk.Web");
        project.Descendants("Sdk").Should().BeEmpty($"{projectName} không được thêm SDK phụ qua phần tử <Sdk>");
        project.Descendants("FrameworkReference").Select(e => (string?)e.Attribute("Include")).Should().BeEmpty(
            $"{projectName} không được tham chiếu shared framework (Microsoft.AspNetCore.App...)");
    }

    /// <summary>Application không được tham chiếu ngược lên Infrastructure, Runtime.Api hay Strategies.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Application_DoesNotReferenceInfrastructureOrApi()
    {
        var references = ReadProjectReferences(Application);

        references.Should().NotContain(
            [Infrastructure, RuntimeApi, DefaultStrategy, MockAlternativeStrategy],
            "Application là lõi use case, chỉ được phụ thuộc SDK");
    }

    private static string[] ReadProjectReferences(string projectName) =>
        LoadProject(projectName)
            .Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToArray();

    private static string[] ReadPackageReferences(string projectName) =>
        LoadProject(projectName)
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .ToArray();

    private static XDocument LoadProject(string projectName)
    {
        var path = RepositoryPaths.Resolve("src", projectName, $"{projectName}.csproj");
        File.Exists(path).Should().BeTrue($"csproj của {projectName} phải tồn tại tại {path}");
        return XDocument.Load(path);
    }
}
