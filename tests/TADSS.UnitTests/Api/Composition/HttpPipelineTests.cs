using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using Swashbuckle.AspNetCore.Swagger;
using TADSS.Application.Decisions;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Controllers;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-02 / UT-08 / UT-09 / UT-10: pipeline HTTP của host thử nghiệm (không PostgreSQL/Redis) — cấu hình JSON v2 của MVC, tập endpoint
/// đúng năm route v2, correlation id đi xuyên success/error, hành vi với Content-Type lạ, Swagger sinh được tài liệu <c>v2</c> và
/// đồ thị DI hợp lệ khi bật <c>ValidateOnBuild</c>/<c>ValidateScopes</c> như môi trường Development.
/// </summary>
public class HttpPipelineTests : IClassFixture<DecisionsApiFactory>
{
    private const string DecisionsPath = "/api/v2/decisions";

    private readonly DecisionsApiFactory _factory;
    private readonly HttpClient _client;

    public HttpPipelineTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
        _client = factory.CreateClient();
    }

    private static HttpRequestMessage PostRequest(string body, string mediaType, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, "booking-0001");
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        }

        return request;
    }

    /// <summary>Correlation id client gửi được echo ở response header khi thành công, và ở cả header lẫn <c>traceId</c> của problem+json khi lỗi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Pipeline_CorrelationIdEchoedOnSuccessAndError()
    {
        using var success = await _client.SendAsync(PostRequest(ContractSamples.RequestJson, "application/json", "corr-ok-1"));
        _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("SECRET-boom");
        using var failure = await _client.SendAsync(PostRequest(ContractSamples.RequestJson, "application/json", "corr-fail-1"));

        success.StatusCode.Should().Be(HttpStatusCode.OK);
        success.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which.Should().Be("corr-ok-1");
        failure.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        failure.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which.Should().Be("corr-fail-1");
        var body = await failure.Content.ReadAsStringAsync();
        body.Should().NotContain("SECRET-boom");
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be("corr-fail-1");
        problem.RootElement.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
    }

    /// <summary>Không gửi correlation id thì middleware sinh GUID và dùng chung cho response header và <c>traceId</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Pipeline_MissingCorrelationId_GeneratesGuidOnErrorResponse()
    {
        using var response = await _client.SendAsync(PostRequest("{", "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var headerValue = response.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which;
        Guid.TryParse(headerValue, out _).Should().BeTrue();
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("traceId").GetString().Should().Be(headerValue);
    }

    /// <summary>
    /// Content-Type khác JSON giữ mặc định của MVC: 415 problem+json (không thuộc tập mã lỗi của contract nên không có <c>code</c>), nhưng
    /// <c>traceId</c> là correlation id đã chuẩn hóa như mọi lỗi khác; service không bị gọi. Khóa lựa chọn 415 thay vì 400.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Post_NonJsonContentType_Is415ProblemWithCorrelationTraceId_Theory(string mediaType)
    {
        using var response = await _client.SendAsync(PostRequest(ContractSamples.RequestJson, mediaType, "corr-media"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        var responseMediaType = response.Content.Headers.ContentType?.MediaType;
        responseMediaType.Should().Be("application/problem+json");
        response.Headers.GetValues(HttpHeaderNames.CorrelationId).Should().ContainSingle().Which.Should().Be("corr-media");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(415);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be("corr-media");
        _factory.DecisionService.Calls.Should().BeEmpty();
    }

    /// <summary>Options JSON của MVC không có converter ngoài và <c>TadssJson.Apply</c> chạy lại không ném (idempotent), cấu hình đúng chuẩn v2.</summary>
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void JsonOptions_DoNotContainJsonStringEnumConverter()
    {
        var options = _factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

        options.Converters.OfType<JsonStringEnumConverter>().Should().BeEmpty();
        options.Converters.Should().BeEmpty("converter chặt gắn trên kiểu/thuộc tính, không có converter toàn cục");
        options.PropertyNameCaseInsensitive.Should().BeFalse();
        options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.NumberHandling.Should().Be(JsonNumberHandling.Strict);
        options.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow);
        var apply = () => TadssJson.Apply(new JsonSerializerOptions(options));
        apply.Should().NotThrow();
    }

    /// <summary>
    /// Chỉ có đúng 5 endpoint controller v2 (đánh giá, chi tiết, hàng chờ, override, outcome), cộng một route catch-all ẩn khỏi OpenAPI trả 404 <c>DECISION_NOT_FOUND</c>
    /// cho định danh sai hình thức (CT-03), và không có route v1 nào.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Routes_OnlyV2DecisionEndpointsExist()
    {
        var allActions = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>().ToList();
        var catchAll = allActions.Where(action => action.ControllerTypeInfo.AsType() == typeof(UnmatchedDecisionRoutesController)).ToList();
        var actions = allActions.Except(catchAll)
            .Select(action => (
                Method: action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).Single(),
                Template: action.AttributeRouteInfo?.Template))
            .ToList();
        var allRoutePatterns = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        actions.Should().BeEquivalentTo(new[]
        {
            (Method: (string?)"POST", Template: (string?)"api/v2/decisions"),
            (Method: (string?)"GET", Template: (string?)"api/v2/decisions/{decisionId:guid}"),
            (Method: (string?)"GET", Template: (string?)"api/v2/decisions/pending-approvals"),
            (Method: (string?)"POST", Template: (string?)"api/v2/decisions/{decisionId:guid}/override"),
            (Method: (string?)"POST", Template: (string?)"api/v2/decisions/{decisionId:guid}/outcome")
        });
        catchAll.Should().ContainSingle().Which.AttributeRouteInfo!.Template.Should().Be("api/v2/decisions/{identifier}/{**rest}");
        allRoutePatterns.Should().NotContain(pattern => pattern.Contains("v1", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Swagger sinh được tài liệu <c>v2</c> không cần DB: đúng 5 path, header idempotency bắt buộc ở mọi POST, đủ mã phản hồi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-09")]
    public void Swagger_GeneratesV2Document_WithHeadersAndErrorResponses()
    {
        var document = _factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v2");

        document.Info.Version.Should().Be("2.0.0");
        document.Paths.Keys.Should().BeEquivalentTo(
        [
            "/api/v2/decisions",
            "/api/v2/decisions/{decisionId}",
            "/api/v2/decisions/pending-approvals",
            "/api/v2/decisions/{decisionId}/override",
            "/api/v2/decisions/{decisionId}/outcome"
        ]);
        var post = document.Paths["/api/v2/decisions"].Operations.Should().ContainSingle().Which.Value;
        var get = document.Paths["/api/v2/decisions/{decisionId}"].Operations.Should().ContainSingle().Which.Value;
        var overrideOperation = document.Paths["/api/v2/decisions/{decisionId}/override"].Operations.Should().ContainSingle().Which.Value;
        post.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "409", "413", "415", "429", "500", "503", "504"]);
        get.Responses.Keys.Should().BeEquivalentTo(["200", "401", "404", "429", "500", "503"]);
        overrideOperation.Responses.Keys.Should().BeEquivalentTo(["200", "400", "401", "404", "409", "413", "415", "429", "500", "503"]);
        overrideOperation.Parameters.Should().ContainSingle(p => p.Name == HttpHeaderNames.ActorId).Which.Required.Should().BeTrue();
        var idempotency = post.Parameters.Should().ContainSingle(p => p.Name == HttpHeaderNames.IdempotencyKey).Which;
        idempotency.Required.Should().BeTrue();
        post.Parameters.Should().ContainSingle(p => p.Name == HttpHeaderNames.CorrelationId).Which.Required.Should().BeFalse();
        get.Parameters.Should().NotContain(p => p.Name == HttpHeaderNames.IdempotencyKey);
        get.Parameters.Should().ContainSingle(p => p.Name == HttpHeaderNames.CorrelationId).Which.Required.Should().BeFalse();
    }

    /// <summary>
    /// Host thật (không thay double, cấu hình mặc định) dựng được khi bật <c>ValidateOnBuild</c> và <c>ValidateScopes</c> — mặc định của
    /// môi trường Development — nên đồ thị DI mới (controller, service scoped, kho audit) không làm app Development vỡ; không chạm DB/Redis.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-10")]
    public void ServiceGraph_ValidatesOnBuildAndScopes_AsInDevelopment()
    {
        using var realServices = new DecisionsApiFactory { UseDoubles = false };
        using var factory = realServices.WithWebHostBuilder(builder => builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            }));

        var start = () => factory.CreateClient();

        start.Should().NotThrow();
    }
}
