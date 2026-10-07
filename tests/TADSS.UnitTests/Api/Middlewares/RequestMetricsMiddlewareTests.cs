using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.Api.Security;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Middlewares;

/// <summary>
/// OB-5: <c>RequestMetricsMiddleware</c> ghi đúng một số đo <c>tadss.decision.duration</c> cho mỗi request <c>/api/*</c> với mẫu route (không phải đường dẫn thật)
/// và status cuối, kể cả khi status do exception handler dựng hoặc request bị từ chối trước routing; không đo health và Swagger.
/// </summary>
public class RequestMetricsMiddlewareTests : IClassFixture<DecisionsApiFactory>
{
    private const string PostEndpoint = "POST api/v2/decisions";
    private const string GetEndpoint = "GET api/v2/decisions/{decisionId}";
    private static readonly Guid DecisionId = Guid.Parse("7c1f5e9a-0000-4000-8000-00000000b001");

    private readonly DecisionsApiFactory _factory;

    public RequestMetricsMiddlewareTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static HttpRequestMessage Post(string? idempotencyKey = "booking-metrics-1")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SecurityTestHost.DecisionsPath)
        {
            Content = new StringContent(ContractSamples.RequestJson, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        return request;
    }

    private static MetricRecorder Record(WebApplicationFactory<Program> factory)
    {
        _ = factory.Server;
        return new MetricRecorder(factory.Services.GetRequiredService<TadssMetrics>().Meter);
    }

    /// <summary>
    /// 200 (POST), 200 (GET theo id), 400 (thiếu khóa idempotency), 409 và 503 (exception → status do handler dựng, endpoint vẫn là mẫu route):
    /// đúng một số đo mỗi request với <c>endpoint</c> và <c>status_code</c> đúng.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData("post-200", PostEndpoint, 200)]
    [InlineData("get-200", GetEndpoint, 200)]
    [InlineData("post-400", PostEndpoint, 400)]
    [InlineData("post-409", PostEndpoint, 409)]
    [InlineData("post-503", PostEndpoint, 503)]
    [InlineData("get-404", GetEndpoint, 404)]
    public async Task RequestMetrics_RecordsEndpointTemplateAndFinalStatus_Theory(string scenario, string expectedEndpoint, int expectedStatus)
    {
        using var client = _factory.CreateClient();
        using var recorder = Record(_factory);
        _factory.DecisionService.Behavior = _ => scenario switch
        {
            "post-409" => throw new IdempotencyConflictException("booking-metrics-1", "Khóa đã dùng với payload khác."),
            "post-503" => throw new PersistenceUnavailableException("Mất kết nối.", new InvalidOperationException("db")),
            _ => Task.FromResult(FakeDecisionService.SampleResult())
        };
        _factory.QueryService.Behavior = _ => scenario == "get-404"
            ? throw new DecisionNotFoundException(DecisionId)
            : Task.FromResult(FakeDecisionQueryService.SampleDetail());
        using var request = scenario.StartsWith("get", StringComparison.Ordinal)
            ? new HttpRequestMessage(HttpMethod.Get, $"{SecurityTestHost.DecisionsPath}/{DecisionId}")
            : Post(scenario == "post-400" ? null : "booking-metrics-1");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
        recorder.Count(TadssMetricNames.DecisionDuration).Should().Be(1, "mỗi request /api/* đúng một số đo");
        recorder.Count(
                TadssMetricNames.DecisionDuration,
                (TadssMetricNames.LabelEndpoint, expectedEndpoint),
                (TadssMetricNames.LabelStatusCode, expectedStatus.ToString()))
            .Should().Be(1);
        recorder.Values(TadssMetricNames.DecisionDuration).Should().OnlyContain(value => value >= 0);
    }

    /// <summary>
    /// Request bị từ chối trước routing — thiếu khóa (401), body quá lớn (413) — mang nhãn <c>"{METHOD} unmatched"</c>, status đúng;
    /// đường dẫn thật (chứa id) không bao giờ thành nhãn.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData("unauthorized", 401)]
    [InlineData("too-large", 413)]
    public async Task RequestMetrics_RejectedBeforeRouting_RecordsUnmatched_Theory(string scenario, int expectedStatus)
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var recorder = Record(_factory);
        using var request = scenario == "too-large"
            ? SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(65537), TestApiKeys.DefaultKey)
            : SecurityTestHost.Post(ContractSamples.RequestJson);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
        recorder.Count(
                TadssMetricNames.DecisionDuration,
                (TadssMetricNames.LabelEndpoint, "POST unmatched"),
                (TadssMetricNames.LabelStatusCode, expectedStatus.ToString()))
            .Should().Be(1);
        recorder.Count(TadssMetricNames.DecisionDuration).Should().Be(1);
    }

    /// <summary>Vượt giới hạn tốc độ (429) cũng được đo, nhãn <c>unmatched</c> vì bị từ chối trước routing.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task RequestMetrics_RateLimited_RecordsUnmatched429()
    {
        using var factory = SecurityTestHost.WithEnvironment(
            _factory,
            DecisionsApiFactory.EnvironmentName,
            ("Tadss:Security:RateLimiting:PermitLimit", "1"),
            ("Tadss:Security:RateLimiting:TokensPerPeriod", "1"),
            ("Tadss:Security:RateLimiting:ReplenishmentPeriodSeconds", "3600"));
        using var client = factory.CreateClient();
        using var recorder = Record(factory);

        using var first = await client.SendAsync(Post());
        using var second = await client.SendAsync(Post());

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        recorder.Count(TadssMetricNames.DecisionDuration, (TadssMetricNames.LabelEndpoint, PostEndpoint), (TadssMetricNames.LabelStatusCode, "200"))
            .Should().Be(1);
        recorder.Count(TadssMetricNames.DecisionDuration, (TadssMetricNames.LabelEndpoint, "POST unmatched"), (TadssMetricNames.LabelStatusCode, "429"))
            .Should().Be(1);
    }

    /// <summary><c>/health/live</c>, <c>/health/ready</c> và Swagger (Development) không được đo.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData("/health/live", "Testing")]
    [InlineData("/health/ready", "Testing")]
    [InlineData("/swagger/v2/swagger.json", "Development")]
    public async Task RequestMetrics_HealthAndSwagger_NotRecorded(string path, string environment)
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, environment)
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear())));
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        using var recorder = Record(factory);

        using var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        recorder.Count(TadssMetricNames.DecisionDuration).Should().Be(0);
    }
}
