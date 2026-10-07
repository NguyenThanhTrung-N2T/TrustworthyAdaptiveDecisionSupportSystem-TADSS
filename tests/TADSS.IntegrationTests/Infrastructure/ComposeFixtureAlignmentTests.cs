using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.UnitTests.TestData;
using Xunit;
using YamlDotNet.Serialization;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// T5.30 / OB-2: <c>docker-compose.yml</c> (một lệnh <c>docker compose up -d --build</c> khởi động tadss-runtime ở Development, ngrok và Grafana, không profile)
/// và <c>tools/expose/verify-expose.sh</c> phải khớp với phần còn lại của repo. File được phân tích <b>theo từng service</b> bằng YamlDotNet (không quét văn bản cả file):
/// các ràng buộc bảo mật áp cho <c>tadss-runtime</c> (không mount, migration literal <c>"false"</c>, không biến secret), còn <c>grafana</c> được mount chỉ đọc dashboard + provisioning
/// và <c>tadss-runtime</c> gửi telemetry tới nó. Mọi cổng host chỉ bind loopback, image ngrok/Grafana ghim tag cụ thể, mọi service nối mạng external <c>smashcourt-network</c>.
/// Chỉ đọc file, không cần Docker.
/// </summary>
public class ComposeFixtureAlignmentTests
{
    private const string LoopbackPrefix = "127.0.0.1:";
    private const string ComposeFile = "docker-compose.yml";
    private const string Runtime = "tadss-runtime";
    private const string ProvisioningTarget = "/otel-lgtm/grafana/conf/provisioning/dashboards/tadss.yaml";
    private const string DashboardsTarget = "/otel-lgtm/tadss-dashboards";

    private static readonly string[] SecretVariableFragments =
    [
        "ConnectionStrings__", "RedisConnectionString", "ApiKeys", "TADSS_DB_CONNECTION", "TADSS_REDIS_CONNECTION", "TADSS_API_KEY_SHA256",
    ];

    /// <summary>Thư mục gốc repo chỉ có đúng một file <c>docker-compose*.yml</c> (không còn file override ngrok/neon hay lab riêng).</summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void RepositoryRoot_HasSingleComposeFile()
    {
        var composeFiles = Directory.GetFiles(RepositoryPaths.Resolve(), "docker-compose*.yml").Select(Path.GetFileName);

        composeFiles.Should().Equal(ComposeFile);
    }

    /// <summary>Mọi mục <c>ports</c> của mọi service đều bắt đầu bằng <c>127.0.0.1:</c>: không có <c>0.0.0.0</c> và không có cổng trần (mặc định bind mọi giao diện).</summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void Compose_BindsOnlyLoopback()
    {
        var ports = Services().SelectMany(service => List(service.Value, "ports")).ToList();

        ports.Should().HaveCount(5, "tadss-runtime (1), trang theo dõi ngrok (1) và Grafana + OTLP gRPC + OTLP HTTP (3)");
        ports.Should().OnlyContain(entry => entry.StartsWith(LoopbackPrefix, StringComparison.Ordinal), "mọi cổng của compose phải bind {0}", LoopbackPrefix);
    }

    /// <summary>
    /// <c>tadss-runtime</c>: <c>ApplyMigrationsOnStartup</c> là literal <c>"false"</c> (không đọc từ biến, không bao giờ tự migrate Neon) và môi trường là <c>Development</c>
    /// (giống SmashCourt-BE: container nạp appsettings.Development.json đã copy vào image).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Runtime_RunsDevelopmentAndForcesMigrationsOff()
    {
        var environment = Map(Service(Runtime), "environment");

        environment["Tadss__Database__ApplyMigrationsOnStartup"].Should().Be("false", "migration không bao giờ được chạy lúc khởi động với database thật");
        environment["ASPNETCORE_ENVIRONMENT"].Should().Be("Development");
    }

    /// <summary>
    /// <c>tadss-runtime</c> không mount file nào (cấu hình, kể cả secret, nằm trong appsettings.Development.json đã copy vào image) và không khai báo chuỗi kết nối, Redis hay API key
    /// trong <c>environment</c> (secret không bị nhân đôi). Ràng buộc áp riêng cho service này: Grafana được mount dashboard (không chứa secret).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Runtime_MountsNothingAndDeclaresNoSecretVariable()
    {
        var runtime = Service(Runtime);

        runtime.Should().NotContainKey("volumes", "tadss-runtime không được khai báo volumes/mount");
        var environment = Map(runtime, "environment");
        foreach (var fragment in SecretVariableFragments)
        {
            environment.Keys.Should().NotContain(key => key.Contains(fragment, StringComparison.Ordinal), "tadss-runtime không được khai báo '{0}'", fragment);
        }

        environment.Values.Should().NotContain(value => value.Contains("appsettings.", StringComparison.Ordinal));
    }

    /// <summary>
    /// OB-4 / OB-2: <c>tadss-runtime</c> bật telemetry và gửi OTLP tới service <c>grafana</c> (<c>http://grafana:4317</c>); <c>grafana</c> chỉ mount hai đường dẫn, đều chỉ đọc:
    /// file provisioning <c>tadss.yaml</c> và thư mục dashboard, đúng đường dẫn provisioning của otel-lgtm (spike S-1).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-2")]
    public void Compose_RuntimeSendsTelemetryToGrafana_GrafanaMountsOnlyDashboardsReadOnly()
    {
        var environment = Map(Service(Runtime), "environment");
        environment["Tadss__Telemetry__Enabled"].Should().Be("true");
        environment["Tadss__Telemetry__OtlpEndpoint"].Should().Be("http://grafana:4317");

        var volumes = List(Service("grafana"), "volumes");
        volumes.Should().HaveCount(2);
        volumes.Should().OnlyContain(volume => volume.EndsWith(":ro", StringComparison.Ordinal), "mount dashboard phải chỉ đọc");
        volumes.Should().Contain(volume => volume == $"./docker/grafana/provisioning/dashboards/tadss.yaml:{ProvisioningTarget}:ro");
        volumes.Should().Contain(volume => volume == $"./docker/grafana/dashboards:{DashboardsTarget}:ro");
        File.Exists(RepositoryPaths.Resolve("docker", "grafana", "provisioning", "dashboards", "tadss.yaml")).Should().BeTrue();
        Directory.Exists(RepositoryPaths.Resolve("docker", "grafana", "dashboards")).Should().BeTrue();
    }

    /// <summary>
    /// OB-2: provider trong <c>tadss.yaml</c> đọc đúng thư mục được mount, vào thư mục "TADSS", không cho sửa trên giao diện; dashboard có <c>uid</c> <c>tadss-runtime</c>
    /// và nằm trong thư mục được mount (một file duy nhất).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-2")]
    public void Dashboard_UidAndProvisioningPathConsistent()
    {
        var provisioning = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(File.ReadAllText(RepositoryPaths.Resolve("docker", "grafana", "provisioning", "dashboards", "tadss.yaml")));
        var provider = ((List<object>)provisioning["providers"]).Cast<Dictionary<object, object>>().Should().ContainSingle().Subject;

        ((Dictionary<object, object>)provider["options"])["path"].Should().Be(DashboardsTarget);
        provider["folder"].Should().Be("TADSS");
        provider["allowUiUpdates"].Should().Be("false");
        provider["type"].Should().Be("file");

        var files = Directory.GetFiles(RepositoryPaths.Resolve("docker", "grafana", "dashboards"), "*.json");
        Path.GetFileName(files.Should().ContainSingle().Subject).Should().Be("tadss-runtime.json");
        File.ReadAllText(files[0]).Should().MatchRegex("\"uid\": \"tadss-runtime\"");
    }

    /// <summary>
    /// Một lệnh <c>docker compose up -d --build</c> khởi động cả ba service (giống SmashCourt-BE): không service nào nằm sau profile.
    /// ngrok chỉ cần <c>NGROK_AUTHTOKEN</c> (không domain tĩnh): lệnh <c>http tadss-runtime:8080</c>, không <c>--url</c>, không <c>NGROK_DOMAIN</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_StartsAllServicesWithoutProfiles_NgrokNeedsOnlyAuthtoken()
    {
        Services().Select(service => service.Key).Should().BeEquivalentTo(["tadss-runtime", "ngrok", "grafana"]);
        Services().Should().NotContain(service => service.Value.ContainsKey("profiles"), "không service nào được nằm sau profile");

        var ngrok = Service("ngrok");
        Scalar(ngrok, "command").Should().Be("http tadss-runtime:8080", "ngrok chuyển tiếp tới tadss-runtime:8080 trong mạng compose");
        Scalar(ngrok, "command").Should().NotContain("--url");
        Map(ngrok, "environment").Should().ContainKey("NGROK_AUTHTOKEN").WhoseValue.Should().Be("${NGROK_AUTHTOKEN}");
        Map(ngrok, "environment").Keys.Should().NotContain("NGROK_DOMAIN");
    }

    /// <summary>Image ngrok và Grafana ghim tag phiên bản cụ thể (không <c>latest</c>, không tên trần); tadss-runtime build từ Dockerfile.</summary>
    [Theory]
    [Trait("CatalogId", "NG-4")]
    [InlineData("ngrok", @"^ngrok/ngrok:\d[\w.\-]*$")]
    [InlineData("grafana", @"^grafana/otel-lgtm:\d[\w.\-]*$")]
    public void Compose_ImageIsPinnedToVersionTag_Theory(string service, string imagePattern)
    {
        var image = Scalar(Service(service), "image");

        image.Should().MatchRegex(imagePattern, "image {0} phải có tag phiên bản cụ thể", service).And.NotContain("latest");
        Service(Runtime).Should().NotContainKey("image", "tadss-runtime build từ Dockerfile");
        Service(Runtime).Should().ContainKey("build");
    }

    /// <summary>
    /// Cả ba service nối mạng external <c>smashcourt-network</c> (cùng quy ước với SmashCourt-BE, tên cố định)
    /// để BE trong Docker gọi được <c>http://tadss-runtime:8080</c> và OTLP gửi được tới Grafana.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void Compose_JoinsExternalSmashCourtNetwork()
    {
        var root = Load();
        var networks = (Dictionary<object, object>)root["networks"];
        var network = (Dictionary<object, object>)networks["smashcourt-network"];
        network["external"].Should().Be("true", "mạng dùng chung với BE/FE phải là external, tên smashcourt-network");
        networks.Should().ContainSingle();

        foreach (var service in Services())
        {
            List(service.Value, "networks").Should().Equal(["smashcourt-network"], "service {0} phải nối mạng smashcourt-network", service.Key);
        }
    }

    /// <summary>Image PostgreSQL/Redis của override tạm trong <c>verify-expose.sh</c> và <c>verify-observability.sh</c> bằng image fixture integration test (cùng phiên bản hạ tầng với test).</summary>
    [Theory]
    [Trait("CatalogId", "UT-09")]
    [InlineData("tools/expose/verify-expose.sh", "expose-postgres", TadssInfrastructureFixture.PostgresImage)]
    [InlineData("tools/expose/verify-expose.sh", "expose-redis", TadssInfrastructureFixture.RedisImage)]
    [InlineData("tools/observability/verify-observability.sh", "obs-postgres", TadssInfrastructureFixture.PostgresImage)]
    [InlineData("tools/observability/verify-observability.sh", "obs-redis", TadssInfrastructureFixture.RedisImage)]
    public void VerifyScriptOverrideImages_MatchFixture_Theory(string scriptPath, string service, string fixtureImage)
    {
        var script = File.ReadAllText(RepositoryPaths.Resolve(scriptPath.Split('/')));

        var match = Regex.Match(script, $@"^  {service}:\r?\n    image: (?<image>\S+)", RegexOptions.Multiline);

        match.Success.Should().BeTrue("{0} phải khai báo service {1} trong override tạm", scriptPath, service);
        match.Groups["image"].Value.Should().Be(fixtureImage, "image {0} của {1} phải bằng image fixture integration test", service, scriptPath);
    }

    private static Dictionary<object, object> Load() =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(RepositoryPaths.Resolve(ComposeFile)));

    private static Dictionary<string, Dictionary<object, object>> Services() =>
        ((Dictionary<object, object>)Load()["services"])
            .ToDictionary(pair => (string)pair.Key, pair => (Dictionary<object, object>)pair.Value);

    private static Dictionary<object, object> Service(string name) => Services()[name];

    private static string Scalar(Dictionary<object, object> service, string key) => (string)service[key];

    private static List<string> List(Dictionary<object, object> service, string key) =>
        service.TryGetValue(key, out var value) ? ((List<object>)value).Select(item => (string)item).ToList() : [];

    private static Dictionary<string, string> Map(Dictionary<object, object> service, string key) =>
        service.TryGetValue(key, out var value)
            ? ((Dictionary<object, object>)value).ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value)
            : [];
}
