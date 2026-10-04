using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog.Events;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// Xác thực <c>X-Api-Key</c> qua pipeline HTTP thật của app (host in-process, service là double): thiếu/sai khóa → 401 problem+json,
/// health được miễn ở mọi môi trường, Swagger chỉ ở Development, log không bao giờ chứa khóa hay mã băm của khóa.
/// </summary>
public class ApiKeyAuthenticationTests : IClassFixture<DecisionsApiFactory>
{
    private const string PostPath = "/api/v2/decisions";
    private const string PendingPath = "/api/v2/decisions/pending-approvals?branchId=1f2e3d4c-0000-4000-8000-000000000001";
    private const string UnknownApiPath = "/api/v2/does-not-exist";
    private const string SwaggerPath = "/swagger/v2/swagger.json";
    private const string DevelopmentSettingsFile = "appsettings.Development.json";
    private const string CorrelationId = "corr-apikey-1";

    private readonly DecisionsApiFactory _factory;

    public ApiKeyAuthenticationTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static HttpRequestMessage Build(string method, string path, string? apiKey)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = new StringContent(ContractSamples.RequestJson, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, "booking-apikey-1");
        }

        request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, CorrelationId);
        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.ApiKey, apiKey);
        }

        return request;
    }

    /// <summary>
    /// Không header, khóa sai, và khóa đúng của client khác đã bị xóa khỏi cấu hình: đều 401 <c>UNAUTHORIZED</c>, <c>traceId</c> trùng
    /// <c>X-Correlation-ID</c> trả về, có <c>WWW-Authenticate</c>; service double không bị gọi (route lạ cũng 401, không lộ route tồn tại).
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-11")]
    [InlineData("POST", PostPath, null)]
    [InlineData("POST", PostPath, "wrong-api-key-value")]
    [InlineData("POST", PostPath, TestApiKeys.SecondKey)]
    [InlineData("GET", PendingPath, null)]
    [InlineData("GET", UnknownApiPath, "wrong-api-key-value")]
    public async Task Request_WithoutOrWithWrongKey_Is401Problem_Theory(string method, string path, string? apiKey)
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = Build(method, path, apiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.WwwAuthenticate.ToString().Should().Be("ApiKey header=\"X-Api-Key\"");
        var correlation = response.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which;
        correlation.Should().Be(CorrelationId);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be(correlation);
        problem.RootElement.GetProperty("instance").GetString().Should().Be(path.Split('?')[0]);
        if (apiKey is not null)
        {
            body.Should().NotContain(apiKey);
        }

        _factory.DecisionService.Calls.Should().BeEmpty();
        _factory.QueryService.RequestedIds.Should().BeEmpty();
    }

    /// <summary>
    /// Nhiều giá trị <c>X-Api-Key</c> (hai header cùng tên) là mơ hồ nên bị từ chối như thiếu khóa dù một trong hai là khóa đúng: 401, service không bị gọi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public async Task Request_WithMultipleApiKeyValues_Is401()
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = Build("POST", PostPath, TestApiKeys.DefaultKey);
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.ApiKey, TestApiKeys.SecondKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should().Be("ApiKey header=\"X-Api-Key\"");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("UNAUTHORIZED");
        _factory.DecisionService.Calls.Should().BeEmpty();
    }

    /// <summary>
    /// Cổng xác thực đọc cùng <c>Request.Path</c> đã giải mã với routing: khác hoa thường, <c>/api</c> trần, dấu gạch chéo cuối, HEAD/OPTIONS đều bị 401 khi thiếu khóa;
    /// đường dẫn lệch chuẩn không khớp route nào (404, không bao giờ 200) và <c>/health/live/x</c> không được hưởng miễn trừ của health.
    /// Service double không bị gọi trong mọi ca.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-4")]
    [InlineData("GET", "/API/v2/decisions", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "/api/v2/decisions/", HttpStatusCode.Unauthorized)]
    [InlineData("HEAD", "/api/v2/decisions/0f9c7e5a-0000-4000-8000-00000000d001", HttpStatusCode.Unauthorized)]
    [InlineData("OPTIONS", "/api/v2/decisions", HttpStatusCode.Unauthorized)]
    [InlineData("GET", "//api/v2/decisions/0f9c7e5a-0000-4000-8000-00000000d001", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api%2Fv2/decisions/0f9c7e5a-0000-4000-8000-00000000d001", HttpStatusCode.NotFound)]
    [InlineData("GET", "/health/live/x", HttpStatusCode.NotFound)]
    public async Task Request_PathVariantsWithoutKey_AreNeverServed_Theory(string method, string path, HttpStatusCode expected)
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(client.BaseAddress!.GetLeftPart(UriPartial.Authority) + path));

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expected);
        _factory.DecisionService.Calls.Should().BeEmpty();
        _factory.QueryService.RequestedIds.Should().BeEmpty();
    }

    /// <summary>Khóa đúng của client mặc định đi qua xác thực tới service double.</summary>
    [Fact]
    [Trait("CatalogId", "IT-11")]
    public async Task Request_WithValidKey_ReachesService()
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = Build("POST", PostPath, TestApiKeys.DefaultKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.DecisionService.Calls.Should().ContainSingle();
    }

    /// <summary>Khóa của client thứ hai (cấu hình có hai client) cũng được chấp nhận.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public async Task Request_WithSecondClientKey_ReachesService()
    {
        using var factory = new DecisionsApiFactory { ApiKeys = [TestApiKeys.Default, TestApiKeys.Second] };
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        using var request = Build("POST", PostPath, TestApiKeys.SecondKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary><c>/health/live</c> và <c>/health/ready</c> không cần khóa (health check của container); không bị 401.</summary>
    [Theory]
    [Trait("CatalogId", "SEC-4")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_IsExemptFromApiKey_Theory(string path)
    {
        // Gỡ mọi health check thật (PostgreSQL/Redis): host này chỉ kiểm tra việc miễn xác thực, không kiểm sức khỏe hạ tầng
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear())));
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Swagger không cần khóa ở <c>Development</c> (200) và không tồn tại ở <c>Testing</c> (404, không phải 401). Host Development
    /// bỏ nguồn <c>appsettings.Development.json</c> nên test không phụ thuộc cấu hình cục bộ của người chạy.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-4")]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Testing", HttpStatusCode.NotFound)]
    public async Task Swagger_ExemptOnlyInDevelopment_Theory(string environment, HttpStatusCode expected)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var developmentSources = configuration.Sources
                    .OfType<JsonConfigurationSource>()
                    .Where(source => source.Path == DevelopmentSettingsFile)
                    .ToList();
                foreach (var source in developmentSources)
                {
                    configuration.Sources.Remove(source);
                }
            });
        });
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        using var response = await client.GetAsync(SwaggerPath);

        response.StatusCode.Should().Be(expected);
    }

    /// <summary>Development chỉ miễn Swagger: <c>/api/*</c> vẫn cần khóa.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public async Task Request_InDevelopmentWithoutKey_StillIs401()
    {
        using var factory = new DecisionsApiFactory { HostEnvironment = "Development" };
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        using var request = Build("POST", PostPath, apiKey: null);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// <c>ApiKeys</c> rỗng ở <c>Testing</c>: host vẫn khởi động, ghi một cảnh báo "mọi request /api/* sẽ bị 401" và quả thật mọi request <c>/api/*</c> bị 401
    /// kể cả khi gửi khóa.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-4")]
    public async Task Host_TestingWithoutApiKeys_WarnsAndRejectsEveryApiRequest()
    {
        var sink = new CapturingLogEventSink();
        using var emptyKeys = new DecisionsApiFactory { ApiKeys = [] };
        using var factory = CapturingLogEventSink.Attach(emptyKeys, sink);
        using var client = factory.CreateClient();
        using var request = Build("POST", PostPath, TestApiKeys.DefaultKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        sink.Events
            .Where(logEvent => logEvent.Level == LogEventLevel.Warning && logEvent.RenderMessage().Contains("mọi request /api/* sẽ bị 401", StringComparison.Ordinal))
            .Should().ContainSingle();
    }

    /// <summary>
    /// Gửi khóa đúng rồi khóa sai: log có <c>ClientId</c> của client khớp, và không dòng log hay property nào chứa khóa, mã băm của khóa
    /// (viết thường lẫn hoa) hay mã băm cấu hình.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-7")]
    public async Task Request_WithKey_LogsClientIdButNeverKey()
    {
        const string wrongKey = "wrong-api-key-for-log-check";
        var sink = new CapturingLogEventSink();
        using var factory = CapturingLogEventSink.Attach(_factory, sink);
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        using (var good = Build("POST", PostPath, TestApiKeys.DefaultKey))
        {
            (await client.SendAsync(good)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var bad = Build("POST", PostPath, wrongKey))
        {
            (await client.SendAsync(bad)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var secrets = new[] { TestApiKeys.DefaultKey, wrongKey }
            .SelectMany(key => new[] { key, TestApiKeys.Sha256Hex(key), TestApiKeys.Sha256Hex(key).ToUpperInvariant() })
            .ToArray();
        var events = sink.Events;
        var quotedClientId = $"\"{TestApiKeys.DefaultClientId}\"";
        events.Count(logEvent => logEvent.Properties.TryGetValue("ClientId", out var value) && value.ToString() == quotedClientId)
            .Should().BePositive("log trong phạm vi request đã xác thực phải mang ClientId");
        events.Count(logEvent => logEvent.Level == LogEventLevel.Information && logEvent.RenderMessage().Contains("Từ chối request", StringComparison.Ordinal))
            .Should().BePositive("request bị từ chối phải để lại dấu vết mức Information (không kèm khóa; client sai khóa là việc thường, không phải cảnh báo của máy chủ)");
        events.Any(logEvent => logEvent.Level >= LogEventLevel.Warning && logEvent.RenderMessage().Contains("Từ chối request", StringComparison.Ordinal))
            .Should().BeFalse("từ chối khóa sai không được ghi ở mức Warning trở lên");
        foreach (var logEvent in events)
        {
            var text = CapturingLogEventSink.FullText(logEvent);
            foreach (var secret in secrets)
            {
                text.Should().NotContain(secret);
            }
        }
    }
}
