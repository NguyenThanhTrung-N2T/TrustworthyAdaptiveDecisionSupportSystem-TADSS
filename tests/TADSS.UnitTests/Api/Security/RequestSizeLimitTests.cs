using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-2: giới hạn kích thước body. <c>Content-Length</c> lớn hơn trần → 413 <c>PAYLOAD_TOO_LARGE</c> trước xác thực và service không bị gọi;
/// đúng bằng trần thì được xử lý; middleware đặt <c>MaxRequestBodySize</c> cho body chunked khi feature cho phép.
/// </summary>
public class RequestSizeLimitTests : IClassFixture<DecisionsApiFactory>
{
    private const int DefaultLimit = 65536;
    private const string CorrelationId = "corr-size-1";

    private readonly DecisionsApiFactory _factory;

    public RequestSizeLimitTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    /// <summary>
    /// Body 64 KB + 1 byte, không gửi khóa: 413 (không phải 401) dạng problem+json có <c>code</c>, <c>traceId</c> = correlation id; service double không được gọi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task RequestSize_ContentLengthOverLimit_Is413BeforeAuth()
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(DefaultLimit + 1), correlationId: CorrelationId);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.Contains("WWW-Authenticate").Should().BeFalse("kiểm tra kích thước chạy trước xác thực");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("PAYLOAD_TOO_LARGE");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(413);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be(CorrelationId);
        _factory.DecisionService.Calls.Should().BeEmpty();
    }

    /// <summary>Body đúng 65536 byte (JSON hợp lệ đệm khoảng trắng) với khóa đúng đi tới service double.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task RequestSize_AtLimit_IsProcessed()
    {
        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(DefaultLimit), TestApiKeys.DefaultKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.DecisionService.Calls.Should().ContainSingle();
    }

    /// <summary>Trần cấu hình được (<c>Tadss:Security:MaxRequestBodyBytes</c>): đặt 2048 thì body 2049 byte bị 413 còn 2048 byte được xử lý.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task RequestSize_ConfiguredLimit_IsHonored()
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, DecisionsApiFactory.EnvironmentName, ("Tadss:Security:MaxRequestBodyBytes", "2048"));
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        using var tooLarge = SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(2049), TestApiKeys.DefaultKey);
        using var atLimit = SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(2048), TestApiKeys.DefaultKey);

        using var tooLargeResponse = await client.SendAsync(tooLarge);
        using var atLimitResponse = await client.SendAsync(atLimit);

        tooLargeResponse.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        atLimitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Request không có body (GET) không bị ảnh hưởng bởi giới hạn.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task RequestSize_RequestWithoutBody_IsNotAffected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/decisions/pending-approvals?branchId=1f2e3d4c-0000-4000-8000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Không có <c>Content-Length</c> (chunked) và feature ghi được: middleware đặt <c>MaxRequestBodySize</c> bằng trần rồi gọi bước kế tiếp.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task Middleware_ChunkedBody_SetsMaxRequestBodySizeFeature()
    {
        var feature = new FakeMaxBodySizeFeature { IsReadOnly = false };
        var (context, middleware, nextCalled) = Arrange(contentLength: null, feature);

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        feature.MaxRequestBodySize.Should().Be(DefaultLimit);
    }

    /// <summary>Feature chỉ đọc (request đã bắt đầu đọc body): middleware không đụng vào và không ném, vẫn gọi bước kế tiếp.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task Middleware_ReadOnlyFeature_IsLeftUntouched()
    {
        var feature = new FakeMaxBodySizeFeature { IsReadOnly = true, MaxRequestBodySize = 123 };
        var (context, middleware, nextCalled) = Arrange(contentLength: 10, feature);

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
        feature.MaxRequestBodySize.Should().Be(123);
    }

    /// <summary>Host không có feature (TestServer): không ném, vẫn gọi bước kế tiếp.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-2")]
    public async Task Middleware_WithoutFeature_CallsNext()
    {
        var (context, middleware, nextCalled) = Arrange(contentLength: 10, feature: null);

        await middleware.InvokeAsync(context);

        nextCalled().Should().BeTrue();
    }

    private static (DefaultHttpContext Context, RequestSizeLimitMiddleware Middleware, Func<bool> NextCalled) Arrange(
        long? contentLength, FakeMaxBodySizeFeature? feature)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentLength = contentLength;
        if (feature is not null)
        {
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        }

        var called = false;
        var middleware = new RequestSizeLimitMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            Microsoft.Extensions.Options.Options.Create(new SecurityOptions { MaxRequestBodyBytes = DefaultLimit }));
        return (context, middleware, () => called);
    }

    private sealed class FakeMaxBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }

        public long? MaxRequestBodySize { get; set; }
    }
}
