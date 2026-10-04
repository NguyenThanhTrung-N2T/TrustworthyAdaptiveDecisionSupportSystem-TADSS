using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// T5.30: <c>docker-compose.yml</c> (cấu hình giống SmashCourt-BE: một lệnh <c>docker compose up -d --build</c> khởi động tadss-runtime ở Development, ngrok và Grafana, không profile)
/// và <c>tools/expose/verify-expose.sh</c> phải khớp với phần còn lại của repo.
/// Chỉ có một file compose ở thư mục gốc, mọi cổng host chỉ bind loopback, migration lúc khởi động luôn tắt, không mount file nào và không có biến chuỗi kết nối/khóa trong compose
/// (appsettings.Development.json được copy vào image local), image ngrok/Grafana ghim tag cụ thể, mọi service nối mạng external <c>smashcourt-network</c>,
/// và image PostgreSQL/Redis của override tạm trong verify-expose.sh bằng image fixture integration test.
/// Chỉ đọc file, không cần Docker.
/// </summary>
public class ComposeFixtureAlignmentTests
{
    private const string LoopbackPrefix = "127.0.0.1:";
    private const string ComposeFile = "docker-compose.yml";

    /// <summary>Thư mục gốc repo chỉ có đúng một file <c>docker-compose*.yml</c> (không còn file override ngrok/neon hay lab riêng).</summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void RepositoryRoot_HasSingleComposeFile()
    {
        var composeFiles = Directory.GetFiles(RepositoryPaths.Resolve(), "docker-compose*.yml").Select(Path.GetFileName);

        composeFiles.Should().Equal(ComposeFile);
    }

    /// <summary>Mọi mục <c>ports</c> đều bắt đầu bằng <c>127.0.0.1:</c>: không có <c>0.0.0.0</c> và không có cổng trần (mặc định bind mọi giao diện).</summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void Compose_BindsOnlyLoopback()
    {
        var compose = ActiveLines(ComposeFile);

        compose.Should().NotContain(line => line.Contains("0.0.0.0", StringComparison.Ordinal), "compose không được bind mọi giao diện");

        var ports = PortEntries(compose);
        ports.Should().HaveCount(5, "tadss-runtime (1), trang theo dõi ngrok (1) và Grafana + OTLP gRPC + OTLP HTTP (3)");
        ports.Should().OnlyContain(entry => entry.StartsWith(LoopbackPrefix, StringComparison.Ordinal),
            "mọi cổng của compose phải bind {0}", LoopbackPrefix);
    }

    /// <summary>
    /// <c>ApplyMigrationsOnStartup</c> là literal <c>"false"</c> (không đọc từ biến, không bao giờ tự migrate Neon) và môi trường là <c>Development</c>
    /// (giống SmashCourt-BE: container nạp appsettings.Development.json đã copy vào image).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_RunsDevelopmentAndForcesMigrationsOff()
    {
        var compose = ActiveLines(ComposeFile);

        compose.Should().Contain(line => line.Trim() == "Tadss__Database__ApplyMigrationsOnStartup: \"false\"",
            "migration không bao giờ được chạy lúc khởi động với database thật");
        compose.Should().NotContain(line => line.Contains("ApplyMigrationsOnStartup", StringComparison.Ordinal) && line.Contains("${", StringComparison.Ordinal),
            "giá trị phải là literal, không đọc từ biến môi trường");
        compose.Should().Contain(line => line.Trim() == "ASPNETCORE_ENVIRONMENT: Development", "container nạp cấu hình từ appsettings.Development.json trong image");
    }

    /// <summary>Compose không mount file nào: cấu hình (kể cả secret) nằm trong appsettings.Development.json đã copy vào image, không gắn thêm từ máy chủ.</summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_DoesNotMountVolumes()
    {
        var compose = ActiveLines(ComposeFile);

        compose.Should().NotContain(line => line.Trim().StartsWith("volumes:", StringComparison.Ordinal), "compose không được khai báo volumes/mount");
        compose.Should().NotContain(line => line.Contains("appsettings.", StringComparison.Ordinal), "compose không tham chiếu file appsettings nào");
    }

    /// <summary>Compose không khai báo chuỗi kết nối, Redis hay API key trong <c>environment</c>: bí mật chỉ nằm trong appsettings.Development.json của image local, không bị nhân đôi.</summary>
    [Theory]
    [Trait("CatalogId", "NG-1")]
    [InlineData("ConnectionStrings__")]
    [InlineData("RedisConnectionString")]
    [InlineData("ApiKeys")]
    [InlineData("TADSS_DB_CONNECTION")]
    [InlineData("TADSS_REDIS_CONNECTION")]
    [InlineData("TADSS_API_KEY_SHA256")]
    public void Compose_DoesNotDeclareSecretVariable_Theory(string fragment)
    {
        var compose = ActiveLines(ComposeFile);

        compose.Should().NotContain(line => line.Contains(fragment, StringComparison.Ordinal),
            "compose không được khai báo '{0}': secret chỉ đến từ appsettings.Development.json trong image", fragment);
    }

    /// <summary>
    /// Một lệnh <c>docker compose up -d --build</c> khởi động cả ba service (giống SmashCourt-BE): không service nào nằm sau profile.
    /// ngrok chỉ cần <c>NGROK_AUTHTOKEN</c> (không domain tĩnh): lệnh <c>http tadss-runtime:8080</c>, không <c>--url</c>, không <c>NGROK_DOMAIN</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_StartsAllServicesWithoutProfiles_NgrokNeedsOnlyAuthtoken()
    {
        var compose = ActiveLines(ComposeFile);

        compose.Should().NotContain(line => line.Contains("profiles:", StringComparison.Ordinal), "không service nào được nằm sau profile");
        compose.Should().NotContain(line => line.Contains("NGROK_DOMAIN", StringComparison.Ordinal) || line.Contains("--url", StringComparison.Ordinal),
            "không dùng domain tĩnh của ngrok");
        ServiceBlock("ngrok").Should().Contain(line => line.Trim() == "command: http tadss-runtime:8080", "ngrok chuyển tiếp tới tadss-runtime:8080 trong mạng compose");
        ServiceBlock("ngrok").Should().Contain(line => line.Trim() == "NGROK_AUTHTOKEN: ${NGROK_AUTHTOKEN}", "authtoken là biến bắt buộc duy nhất của ngrok");
    }

    /// <summary>Image ngrok và Grafana ghim tag phiên bản cụ thể (không <c>latest</c>, không tên trần); đổi bản bằng cách sửa tag trong file compose.</summary>
    [Theory]
    [Trait("CatalogId", "NG-4")]
    [InlineData("ngrok", @"^ngrok/ngrok:\d[\w.\-]*$")]
    [InlineData("grafana", @"^grafana/otel-lgtm:\d[\w.\-]*$")]
    public void Compose_ImageIsPinnedToVersionTag_Theory(string service, string imagePattern)
    {
        var images = ServiceBlock(service)
            .Select(line => Regex.Match(line, @"^\s+image: (?<image>\S+)\s*$"))
            .Where(match => match.Success)
            .Select(match => match.Groups["image"].Value)
            .ToList();

        images.Should().ContainSingle("service {0} phải khai báo đúng một image", service);
        images[0].Should().MatchRegex(imagePattern, "image {0} phải có tag phiên bản cụ thể", service).And.NotContain("latest");
        ActiveLines(ComposeFile).Count(line => line.TrimStart().StartsWith("image:", StringComparison.Ordinal))
            .Should().Be(2, "tadss-runtime build từ Dockerfile, chỉ ngrok và Grafana dùng image dựng sẵn");
    }

    /// <summary>
    /// Cả ba service nối mạng external <c>smashcourt-network</c> (cùng quy ước với SmashCourt-BE, tên cố định)
    /// để BE trong Docker gọi được <c>http://tadss-runtime:8080</c> và OTLP gửi được tới Grafana.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_JoinsExternalSmashCourtNetwork()
    {
        var joined = string.Join("\n", ActiveLines(ComposeFile));

        joined.Should().MatchRegex(@"(?m)^networks:\n  smashcourt-network:\n    external: true\s*$",
            "mạng dùng chung với BE/FE phải là external, tên smashcourt-network");
        Regex.Matches(joined, @"(?m)^    networks:\n      - smashcourt-network$").Should()
            .HaveCount(3, "tadss-runtime, ngrok và grafana đều phải nối mạng smashcourt-network");
    }

    /// <summary>Image PostgreSQL/Redis của override tạm trong <c>verify-expose.sh</c> bằng image fixture integration test (cùng phiên bản hạ tầng với test).</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("expose-postgres", TadssInfrastructureFixture.PostgresImage)]
    [InlineData("expose-redis", TadssInfrastructureFixture.RedisImage)]
    public void VerifyExposeOverrideImages_MatchFixture_Theory(string service, string fixtureImage)
    {
        var script = ReadRepositoryFile("tools/expose/verify-expose.sh");

        var match = Regex.Match(script, $@"^  {service}:\r?\n    image: (?<image>\S+)", RegexOptions.Multiline);

        match.Success.Should().BeTrue("verify-expose.sh phải khai báo service {0} trong override tạm", service);
        match.Groups["image"].Value.Should().Be(fixtureImage, "image {0} của verify-expose.sh phải bằng image fixture integration test", service);
    }

    private static string ReadRepositoryFile(string relativePath) => File.ReadAllText(RepositoryPaths.Resolve(relativePath.Split('/')));

    /// <summary>Các dòng không phải comment, đã bỏ comment cuối dòng (comment được phép nhắc tới 0.0.0.0 để giải thích).</summary>
    private static List<string> ActiveLines(string relativePath) =>
        ReadRepositoryFile(relativePath).Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .Select(line => Regex.Replace(line, @"\s+#.*$", string.Empty))
            .ToList();

    /// <summary>Các dòng của một service trong khối <c>services:</c> (từ dòng <c>  name:</c> tới service kế tiếp hoặc hết khối).</summary>
    private static List<string> ServiceBlock(string service)
    {
        var block = new List<string>();
        var inBlock = false;
        foreach (var line in ActiveLines(ComposeFile))
        {
            if (Regex.IsMatch(line, @"^  \S"))
            {
                inBlock = line.TrimEnd() == $"  {service}:";
            }
            else if (line.Length > 0 && !line.StartsWith(' '))
            {
                inBlock = false;
            }

            if (inBlock)
            {
                block.Add(line);
            }
        }

        return block;
    }

    /// <summary>Mọi mục (<c>- "…"</c>) nằm trong một khối <c>ports:</c>.</summary>
    private static List<string> PortEntries(List<string> lines)
    {
        var entries = new List<string>();
        var inPorts = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ports:", StringComparison.Ordinal))
            {
                inPorts = true;
            }
            else if (inPorts && trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                entries.Add(trimmed[2..].Trim().Trim('"'));
            }
            else if (trimmed.Length > 0)
            {
                inPorts = false;
            }
        }

        return entries;
    }
}
