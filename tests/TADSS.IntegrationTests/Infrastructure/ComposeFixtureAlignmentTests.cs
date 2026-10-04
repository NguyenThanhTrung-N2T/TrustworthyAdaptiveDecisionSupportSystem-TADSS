using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// T5.30: các file docker compose phải khớp với phần còn lại của repo. Image mặc định của E-LOCAL bằng image fixture integration test
/// (test chạy trên đúng phiên bản hạ tầng mà môi trường local dùng), khóa API local công khai bằng hằng của validator, override ngrok chỉ bind
/// loopback và override neon luôn tắt migration lúc khởi động. Chỉ đọc file compose, không cần Docker.
/// </summary>
public class ComposeFixtureAlignmentTests
{
    private const string LoopbackPrefix = "127.0.0.1:";

    /// <summary>Mặc định của biến image PostgreSQL/Redis/Toxiproxy trong <c>docker-compose.yml</c> bằng hằng của <see cref="TadssInfrastructureFixture"/>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("TADSS_POSTGRES_IMAGE", TadssInfrastructureFixture.PostgresImage)]
    [InlineData("TADSS_REDIS_IMAGE", TadssInfrastructureFixture.RedisImage)]
    [InlineData("TADSS_TOXIPROXY_IMAGE", TadssInfrastructureFixture.ToxiproxyImage)]
    public void ComposeImageDefaults_MatchFixture_Theory(string variable, string fixtureImage)
    {
        var compose = ReadCompose("docker-compose.yml");

        var match = Regex.Match(compose, $@"\$\{{{variable}:-(?<default>[^}}]+)\}}");

        match.Success.Should().BeTrue("docker-compose.yml phải khai báo {0} với giá trị mặc định", variable);
        match.Groups["default"].Value.Should().Be(fixtureImage, "image mặc định của {0} phải bằng image fixture integration test", variable);
    }

    /// <summary>Mã băm mặc định của <c>TADSS_API_KEY_SHA256</c> bằng <see cref="SecurityOptionsValidator.LocalDevelopmentKeySha256"/> (khóa local công khai, F-18).</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void ComposeLocalApiKey_MatchesValidatorConstant()
    {
        var compose = ReadCompose("docker-compose.yml");

        var match = Regex.Match(compose, @"\$\{TADSS_API_KEY_SHA256:-(?<default>[^}]+)\}");

        match.Success.Should().BeTrue("docker-compose.yml phải đặt mặc định cho TADSS_API_KEY_SHA256");
        match.Groups["default"].Value.Should().Be(SecurityOptionsValidator.LocalDevelopmentKeySha256);
    }

    /// <summary>
    /// Override ngrok không chứa <c>0.0.0.0</c>, mọi mục <c>ports</c> đều bắt đầu bằng <c>127.0.0.1:</c>, và mọi service có cổng ở base đều bị
    /// <c>!override</c> (không để cổng bind mọi giao diện lọt qua việc gộp danh sách).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void NgrokOverride_BindsOnlyLoopback()
    {
        var ngrok = ActiveLines("docker-compose.ngrok.yml");

        ngrok.Should().NotContain(line => line.Contains("0.0.0.0", StringComparison.Ordinal), "override ngrok không được bind mọi giao diện");

        var ngrokPorts = PortEntries(ngrok);
        ngrokPorts.Should().NotBeEmpty();
        ngrokPorts.Should().OnlyContain(entry => entry.StartsWith(LoopbackPrefix, StringComparison.Ordinal),
            "mọi cổng của override ngrok phải bind {0}", LoopbackPrefix);

        var baseServicesWithPorts = ServicesWithPorts(ActiveLines("docker-compose.yml"));
        var overriddenServices = ServicesWithOverriddenPorts(ngrok);
        overriddenServices.Should().Contain(baseServicesWithPorts, "mọi service có cổng ở base phải bị !override sang loopback");
    }

    /// <summary>Override neon ép <c>ApplyMigrationsOnStartup</c> là literal <c>"false"</c> (không đọc từ biến) và bắt buộc <c>TADSS_DB_CONNECTION</c>.</summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void NeonOverride_ForcesMigrationsOff()
    {
        var neon = ActiveLines("docker-compose.neon.yml");

        neon.Should().Contain(line => line.Trim() == "Tadss__Database__ApplyMigrationsOnStartup: \"false\"",
            "migration không bao giờ được chạy lúc khởi động với database ngoài");
        neon.Should().NotContain(line => line.Contains("ApplyMigrationsOnStartup", StringComparison.Ordinal) && line.Contains("${", StringComparison.Ordinal),
            "giá trị phải là literal, không đọc từ biến môi trường");
        neon.Should().Contain(line => line.Contains("${TADSS_DB_CONNECTION:?", StringComparison.Ordinal),
            "TADSS_DB_CONNECTION phải bắt buộc (cú pháp :?)");
    }

    /// <summary>
    /// Cả hai nhánh lệnh <c>ngrok http</c> phải truyền <c>--config /var/lib/ngrok/ngrok.yml</c>: <c>entrypoint: sh -c</c> bỏ entrypoint gốc của image
    /// (vốn luôn nạp file này, <c>web_addr 0.0.0.0:4040</c>), thiếu cờ thì giao diện kiểm tra chỉ nghe loopback trong container và cổng host không truy cập được.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-4")]
    public void NgrokOverride_InspectorUsesImageConfig()
    {
        var compose = string.Join(Environment.NewLine, ActiveLines("docker-compose.ngrok.yml"));

        var invocations = Regex.Matches(compose, @"exec ngrok http(?<rest> [^;]*)");
        invocations.Should().HaveCount(2, "lệnh có hai nhánh (có và không có NGROK_DOMAIN)");
        invocations.Select(match => match.Groups["rest"].Value).Should()
            .OnlyContain(rest => rest.StartsWith(" --config /var/lib/ngrok/ngrok.yml ", StringComparison.Ordinal),
                "mỗi nhánh ngrok http phải nạp config của image để giao diện kiểm tra nghe 0.0.0.0:4040 trong container");
    }

    /// <summary>
    /// Override neon an toàn khi dùng riêng với base (không kèm ngrok): ép <c>Staging</c>, bắt buộc khóa riêng (<c>:?</c>, không rơi về khóa local công khai)
    /// và chỉ bind cổng host của <c>tadss-runtime</c> vào loopback.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void NeonOverride_IsSafeOnItsOwn()
    {
        var neon = ActiveLines("docker-compose.neon.yml");

        neon.Should().Contain(line => line.Trim() == "ASPNETCORE_ENVIRONMENT: Staging", "neon dùng riêng không được chạy ở Development");
        neon.Should().Contain(line => line.Contains("Tadss__Security__ApiKeys__0__KeySha256: ${TADSS_API_KEY_SHA256:?", StringComparison.Ordinal),
            "khóa API riêng phải bắt buộc, không có mặc định");
        neon.Should().Contain(line => line.Trim() == "ports: !override", "cổng host của base phải bị thay hẳn");
        var ports = PortEntries(neon);
        ports.Should().NotBeEmpty();
        ports.Should().OnlyContain(entry => entry.StartsWith(LoopbackPrefix, StringComparison.Ordinal));
    }

    private static string ReadCompose(string fileName) => File.ReadAllText(RepositoryPaths.Resolve(fileName));

    /// <summary>Các dòng không phải comment (comment được phép nhắc tới 0.0.0.0 để giải thích).</summary>
    private static List<string> ActiveLines(string fileName) =>
        ReadCompose(fileName).Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToList();

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

    /// <summary>Tên service (khóa thụt lề 2 dấu cách dưới <c>services:</c>) có khối <c>ports:</c>, hoặc <c>ports: !override</c> nếu <paramref name="overriddenOnly"/>.</summary>
    private static List<string> ServicesMatching(List<string> lines, bool overriddenOnly)
    {
        var services = new List<string>();
        string? current = null;
        foreach (var line in lines)
        {
            var service = Regex.Match(line, @"^  (?<name>[a-z][\w-]*):\s*$");
            if (service.Success)
            {
                current = service.Groups["name"].Value;
                continue;
            }

            var trimmed = line.Trim();
            var isPorts = overriddenOnly
                ? trimmed == "ports: !override"
                : trimmed.StartsWith("ports:", StringComparison.Ordinal);
            if (current is not null && isPorts)
            {
                services.Add(current);
            }
        }

        return services;
    }

    private static List<string> ServicesWithPorts(List<string> lines) => ServicesMatching(lines, overriddenOnly: false);

    private static List<string> ServicesWithOverriddenPorts(List<string> lines) => ServicesMatching(lines, overriddenOnly: true);
}
