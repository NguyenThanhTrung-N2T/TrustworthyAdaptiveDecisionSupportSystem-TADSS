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
    private const string EvaluationProfile = "evaluation";
    private const string EvaluationGrafanaPort = "127.0.0.1:3002:3000";
    private static readonly string[] OperationsServiceNames = ["tadss-runtime", "ngrok", "grafana"];
    private static readonly string[] EvaluationServiceNames =
        ["eval-postgres", "eval-redis", "eval-toxiproxy", "eval-tadss-1", "eval-tadss-2", "eval-tadss-portability", "eval-proxy", "eval-grafana", "evaluator"];

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

    /// <summary>Mọi mục <c>ports</c> của mọi service (kể cả service đánh giá) đều bắt đầu bằng <c>127.0.0.1:</c>: không có <c>0.0.0.0</c> và không có cổng trần (mặc định bind mọi giao diện).</summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void Compose_BindsOnlyLoopback()
    {
        var ports = Services().SelectMany(service => List(service.Value, "ports")).ToList();

        ports.Should().HaveCount(6, "tadss-runtime (1), trang theo dõi ngrok (1), Grafana + OTLP gRPC + OTLP HTTP (3) và Grafana của stack đánh giá (1)");
        ports.Should().OnlyContain(entry => entry.StartsWith(LoopbackPrefix, StringComparison.Ordinal), "mọi cổng của compose phải bind {0}", LoopbackPrefix);
    }

    /// <summary>Các service đánh giá chỉ publish đúng một cổng ra máy chủ: Grafana ở <c>127.0.0.1:3002</c> (NG-5 áp cho cả profile đánh giá).</summary>
    [Fact]
    [Trait("CatalogId", "NG-5")]
    public void EvaluationProfile_OnlyGrafanaPortOnLoopback()
    {
        var ports = Services().Where(service => IsEvaluation(service.Value))
            .SelectMany(service => List(service.Value, "ports").Select(port => (Service: service.Key, Port: port))).ToList();

        ports.Should().ContainSingle().Which.Should().Be(("eval-grafana", EvaluationGrafanaPort));
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
    /// Một lệnh <c>docker compose up -d --build</c> khởi động đúng ba service vận hành (giống SmashCourt-BE): chỉ ba service này không nằm sau profile, nên profile đánh giá không làm đổi chế độ vận hành.
    /// ngrok chỉ cần <c>NGROK_AUTHTOKEN</c> (không domain tĩnh): lệnh <c>http tadss-runtime:8080</c>, không <c>--url</c>, không <c>NGROK_DOMAIN</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void OperationsServices_UnaffectedByEvaluationProfile()
    {
        Services().Where(service => !service.Value.ContainsKey("profiles")).Select(service => service.Key).Should().BeEquivalentTo(OperationsServiceNames);
        Services().Select(service => service.Key).Should().BeEquivalentTo(OperationsServiceNames.Concat(EvaluationServiceNames));

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

        foreach (var service in Services().Where(service => !IsEvaluation(service.Value)))
        {
            List(service.Value, "networks").Should().Equal(["smashcourt-network"], "service {0} phải nối mạng smashcourt-network", service.Key);
        }
    }

    /// <summary>
    /// Mỗi service đánh giá nằm sau profile <c>evaluation</c> và <b>không</b> nối <c>smashcourt-network</c> (stack cô lập, mạng mặc định riêng). Hai instance TADSS chính chạy <c>Testing</c> và chỉ nối
    /// PostgreSQL/Redis <b>qua</b> <c>eval-toxiproxy</c> (không trỏ thẳng vào <c>eval-postgres</c>/<c>eval-redis</c>, không host ngoài). Instance PT (<c>eval-tadss-portability</c>, gói 5.6) ngược lại nối
    /// <b>thẳng</b> <c>eval-postgres</c>/<c>eval-redis</c> với database <c>tadss_portability</c> riêng, không tiêm lỗi, không cổng host, không mount, cấu hình miền khách sạn chỉ bằng biến môi trường.
    /// Image PostgreSQL/Redis/Toxiproxy/Grafana bằng hằng của fixture hoặc compose vận hành; mọi image dựng sẵn ghim tag cụ thể (không <c>latest</c>); evaluator build từ Dockerfile của nó.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "NG-1")]
    [InlineData("eval-postgres")]
    [InlineData("eval-redis")]
    [InlineData("eval-toxiproxy")]
    [InlineData("eval-tadss-1")]
    [InlineData("eval-tadss-2")]
    [InlineData("eval-tadss-portability")]
    [InlineData("eval-proxy")]
    [InlineData("eval-grafana")]
    [InlineData("evaluator")]
    public void EvaluationServices_Theory(string name)
    {
        var service = Service(name);

        List(service, "profiles").Should().Equal([EvaluationProfile], "{0} phải nằm sau profile evaluation", name);
        service.Should().NotContainKey("networks", "stack đánh giá dùng mạng mặc định riêng, không nối smashcourt-network");
        if (name is not "eval-tadss-1" and not "eval-tadss-2" and not "eval-tadss-portability" and not "evaluator")
        {
            Scalar(service, "image").Should().MatchRegex(@":\d[\w.\-]*$", "image {0} phải có tag phiên bản cụ thể", name).And.NotContain("latest");
        }

        switch (name)
        {
            case "eval-postgres":
                Scalar(service, "image").Should().Be(TadssInfrastructureFixture.PostgresImage);
                break;
            case "eval-redis":
                Scalar(service, "image").Should().Be(TadssInfrastructureFixture.RedisImage);
                break;
            case "eval-toxiproxy":
                Scalar(service, "image").Should().Be(TadssInfrastructureFixture.ToxiproxyImage);
                break;
            case "eval-grafana":
                Scalar(service, "image").Should().Be(Scalar(Service("grafana"), "image"), "cùng image otel-lgtm với chế độ vận hành");
                break;
            case "eval-tadss-1" or "eval-tadss-2":
                var environment = Map(service, "environment");
                environment["ASPNETCORE_ENVIRONMENT"].Should().Be("Testing", "decorator tiêm lỗi chỉ đăng ký ở Testing; Testing không nạp appsettings.Development.json");
                environment["ConnectionStrings__DefaultConnection"].Should().Contain("Host=eval-toxiproxy;").And.NotContain("eval-postgres");
                environment["Tadss__Cache__RedisConnectionString"].Should().StartWith("eval-toxiproxy:");
                environment["Tadss__Database__ApplyMigrationsOnStartup"].Should().Be(name == "eval-tadss-1" ? "true" : "false", "chỉ instance 1 áp migration lên database dùng một lần");
                environment["Tadss__Security__ApiKeys__0__KeySha256"].Should().Be("0065f53b16f1098163cf687e50c0533eb8a546be30b53683cf6194893c7ee406", "SHA-256 của hằng không bí mật tadss-evaluation-key");
                environment["Tadss__Telemetry__OtlpEndpoint"].Should().Be("http://eval-grafana:4317");
                environment["Tadss__Telemetry__ServiceInstanceId"].Should().Be(name == "eval-tadss-1" ? "tadss-1" : "tadss-2");
                environment.ContainsKey("TADSS_FAULT_INJECTION_FILE").Should().Be(name == "eval-tadss-1", "chỉ instance 1 đọc file tiêm lỗi");
                break;
            case "eval-tadss-portability":
                var portability = Map(service, "environment");
                portability["ASPNETCORE_ENVIRONMENT"].Should().Be("Testing");
                portability["ConnectionStrings__DefaultConnection"].Should().Contain("Host=eval-postgres;Port=5432;").And.Contain("Database=tadss_portability;").And.NotContain("toxiproxy");
                portability["Tadss__Cache__RedisConnectionString"].Should().Be("eval-redis:6379");
                portability["Tadss__Cache__InstanceName"].Should().NotBeNullOrWhiteSpace().And.NotBe("tadss:", "tiền tố khóa Redis riêng, không trùng instance 1/2");
                portability["Tadss__Database__ApplyMigrationsOnStartup"].Should().Be("true", "database tadss_portability là của riêng instance này");
                portability["Tadss__Security__ApiKeys__0__KeySha256"].Should().Be("0065f53b16f1098163cf687e50c0533eb8a546be30b53683cf6194893c7ee406", "cùng khóa đánh giá như hai instance kia");
                portability["Tadss__Telemetry__ServiceInstanceId"].Should().Be("tadss-pt");
                portability["Tadss__Telemetry__OtlpEndpoint"].Should().Be("http://eval-grafana:4317");
                portability["Tadss__Validation__MaxItemMinutes"].Should().Be("100800", "phủ lưu trú dài nhất của dataset khách sạn (spike S-2)");
                portability["Tadss__Strategies__Default__HighValueThreshold"].Should().Be("750", "ngưỡng giá trị cao tính bằng EUR (spike S-2)");
                portability.Keys.Should().NotContain(key => key.Contains("FaultInjection", StringComparison.Ordinal) || key == "TADSS_FAULT_INJECTION_FILE", "PT không tiêm lỗi");
                service.Should().NotContainKey("ports", "NG-5: không mở cổng host");
                service.Should().NotContainKey("volumes", "NG-1: không mount file nào");
                Scalar(service, "image").Should().Be(Scalar(Service("eval-tadss-1"), "image"), "cùng image runtime với instance 1/2");
                DependsOn(service).Should().BeEquivalentTo("eval-postgres", "eval-redis", "eval-grafana").And.NotContain("eval-toxiproxy", "PT không đi qua Toxiproxy");
                break;
            case "evaluator":
                service.Should().ContainKey("build");
                List(service, "volumes").Should().Contain(".:/repo:ro", "mã nguồn mount chỉ đọc").And.Contain("/var/run/docker.sock:/var/run/docker.sock")
                    .And.Contain("./data/private:/datasets/private:ro", "CSV SmashCourt thô chỉ đọc").And.Contain("eval-datasets:/datasets/cache");
                var evaluator = Map(service, "environment");
                evaluator["EVAL_MODE"].Should().Be("${EVAL_MODE:-quick}");
                evaluator["EVAL_PT_URL"].Should().Be("http://eval-tadss-portability:8080");
                evaluator["EVAL_PT_PG_DSN"].Should().Contain("dbname=tadss_portability");
                evaluator["EVAL_DATASETS_PRIVATE"].Should().Be("/datasets/private");
                evaluator["EVAL_DATASETS_CACHE"].Should().Be("/datasets/cache");
                evaluator["TADSS_SCHEMA_DIR"].Should().Be("/repo/docs/contracts/schemas/v2");
                DependsOn(service).Should().Contain("eval-tadss-portability", "evaluator chờ instance PT khỏe");
                ((Dictionary<object, object>)Load()["volumes"]).Keys.Cast<string>().Should().Contain("eval-datasets");
                break;
        }
    }

    /// <summary>Thư mục <c>data/private/</c> (CSV SmashCourt thô do người dùng xuất, mount vào evaluator) nằm trong <c>.gitignore</c>: dữ liệu cá nhân không bao giờ vào repo.</summary>
    [Fact]
    [Trait("CatalogId", "NG-1")]
    public void PrivateDataDirectory_IsGitIgnored()
    {
        File.ReadAllLines(RepositoryPaths.Resolve(".gitignore")).Select(line => line.Trim()).Should().Contain("data/private/");
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

    private static bool IsEvaluation(Dictionary<object, object> service) => List(service, "profiles").Contains(EvaluationProfile);

    private static string Scalar(Dictionary<object, object> service, string key) => (string)service[key];

    private static List<string> DependsOn(Dictionary<object, object> service) =>
        service.TryGetValue("depends_on", out var value) ? ((Dictionary<object, object>)value).Keys.Cast<string>().ToList() : [];

    private static List<string> List(Dictionary<object, object> service, string key) =>
        service.TryGetValue(key, out var value) ? ((List<object>)value).Select(item => (string)item).ToList() : [];

    private static Dictionary<string, string> Map(Dictionary<object, object> service, string key) =>
        service.TryGetValue(key, out var value)
            ? ((Dictionary<object, object>)value).ToDictionary(pair => (string)pair.Key, pair => (string)pair.Value)
            : [];
}
