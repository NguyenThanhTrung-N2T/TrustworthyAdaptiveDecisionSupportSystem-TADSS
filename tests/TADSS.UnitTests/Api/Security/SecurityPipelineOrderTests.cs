using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// UT-14: thứ tự các middleware bảo mật (F-09) khóa bằng hành vi quan sát được: kích thước chạy trước xác thực, correlation id và header bảo mật
/// bọc cả response bị từ chối/lỗi, Swagger (Development) và health được miễn khóa.
/// </summary>
public class SecurityPipelineOrderTests : IClassFixture<DecisionsApiFactory>
{
    private const string CorrelationId = "corr-order-1";

    private readonly DecisionsApiFactory _factory;

    public SecurityPipelineOrderTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response, string kind)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle(kind).Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle(kind).Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle(kind).Which.Should().Be("no-referrer");
    }

    /// <summary>
    /// (a) 413 không có khóa → 413, không phải 401; (b) 401 có correlation id và header bảo mật; (c) 500 có correlation id và header bảo mật;
    /// (d) Swagger ở Development không cần khóa; (e) health không cần khóa.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData("a-too-large-before-auth", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("b-unauthorized", HttpStatusCode.Unauthorized)]
    [InlineData("c-internal-error", HttpStatusCode.InternalServerError)]
    [InlineData("d-swagger-in-development", HttpStatusCode.OK)]
    [InlineData("e-health-without-key", HttpStatusCode.OK)]
    public async Task PipelineOrder_ObservableBehaviour_Theory(string kind, HttpStatusCode expectedStatus)
    {
        var factory = kind.StartsWith('d')
            ? SecurityTestHost.WithEnvironment(_factory, "Development")
            : kind.StartsWith('e')
                ? WithoutHealthChecks(_factory)
                : _factory;
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("boom-pipeline-order");
        using var request = kind[0] switch
        {
            'a' => SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(65537), correlationId: CorrelationId),
            'b' => SecurityTestHost.Post(ContractSamples.RequestJson, correlationId: CorrelationId),
            'c' => SecurityTestHost.Post(ContractSamples.RequestJson, TestApiKeys.DefaultKey, correlationId: CorrelationId),
            'd' => new HttpRequestMessage(HttpMethod.Get, "/swagger/v2/swagger.json"),
            _ => new HttpRequestMessage(HttpMethod.Get, "/health/live")
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expectedStatus, kind);
        AssertSecurityHeaders(response, kind);
        response.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle(kind);
        if (kind[0] is 'a' or 'b' or 'c')
        {
            response.Headers.GetValues(HttpHeaderNames.CorrelationId).Single().Should().Be(CorrelationId);
        }

        if (!ReferenceEquals(factory, _factory))
        {
            factory.Dispose();
        }
    }

    /// <summary>Gỡ health check thật (PostgreSQL/Redis): host này chỉ kiểm tra việc miễn xác thực, không kiểm sức khỏe hạ tầng.</summary>
    private static WebApplicationFactory<Program> WithoutHealthChecks(WebApplicationFactory<Program> baseFactory) =>
        baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear())));
}
