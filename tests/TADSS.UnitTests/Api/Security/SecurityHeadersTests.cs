using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-3: header bảo mật có trên mọi loại response (thành công, 401, 404, 415, 500, 413) kể cả response do exception handler ghi;
/// HSTS chỉ ngoài <c>Development</c>/<c>Testing</c>; Kestrel không lộ header <c>Server</c>.
/// </summary>
public class SecurityHeadersTests : IClassFixture<DecisionsApiFactory>
{
    private const string UnknownDecisionPath = "/api/v2/decisions/0f9c7e5a-0000-4000-8000-00000000d099";
    private const string HstsHeader = "Strict-Transport-Security";

    private readonly DecisionsApiFactory _factory;

    public SecurityHeadersTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response, bool expectNoStore, string because)
    {
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle(because).Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle(because).Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle(because).Which.Should().Be("no-referrer");
        if (expectNoStore)
        {
            (response.Headers.CacheControl?.NoStore ?? false).Should().BeTrue(because);
        }
        else
        {
            response.Headers.Contains("Cache-Control").Should().BeFalse(because);
        }
    }

    private static HttpRequestMessage GetWithKey(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.ApiKey, TestApiKeys.DefaultKey);
        return request;
    }

    /// <summary>
    /// 200, 401 (không khóa), 404 <c>DECISION_NOT_FOUND</c>, 415, 500 (service ném) và 413 (Content-Length vượt trần): đủ ba header, <c>Cache-Control: no-store</c> ở <c>/api/*</c>.
    /// 404 và 500 do exception handler ghi nên chứng minh callback <c>OnStarting</c> sống sót qua <c>Response.Clear()</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-3")]
    [InlineData("ok-200", HttpStatusCode.OK)]
    [InlineData("unauthorized-401", HttpStatusCode.Unauthorized)]
    [InlineData("not-found-404", HttpStatusCode.NotFound)]
    [InlineData("unsupported-media-415", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("internal-500", HttpStatusCode.InternalServerError)]
    [InlineData("too-large-413", HttpStatusCode.RequestEntityTooLarge)]
    public async Task SecurityHeaders_PresentOnAllResponseKinds_Theory(string kind, HttpStatusCode expectedStatus)
    {
        if (kind == "not-found-404")
        {
            _factory.QueryService.Behavior = id => throw new DecisionNotFoundException(id);
        }

        if (kind == "internal-500")
        {
            _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("boom-security-headers");
        }

        using var client = TestApiKeys.CreateClientWithoutKey(_factory);
        using var request = kind switch
        {
            "ok-200" => SecurityTestHost.Post(ContractSamples.RequestJson, TestApiKeys.DefaultKey),
            "unauthorized-401" => SecurityTestHost.Post(ContractSamples.RequestJson),
            "not-found-404" => GetWithKey(UnknownDecisionPath),
            "unsupported-media-415" => SecurityTestHost.Post(ContractSamples.RequestJson, TestApiKeys.DefaultKey, "text/plain"),
            "internal-500" => SecurityTestHost.Post(ContractSamples.RequestJson, TestApiKeys.DefaultKey),
            "too-large-413" => SecurityTestHost.Post(SecurityTestHost.RequestJsonOfSize(65537)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Ca không biết.")
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expectedStatus, kind);
        AssertSecurityHeaders(response, expectNoStore: true, kind);
        if (expectedStatus == HttpStatusCode.NotFound)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        }
    }

    /// <summary>Đường dẫn ngoài <c>/api/*</c> (404 mặc định) vẫn có ba header nhưng không bị <c>Cache-Control: no-store</c>.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-3")]
    public async Task SecurityHeaders_NonApiPath_HasNoCacheControl()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/not-an-api-path");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        AssertSecurityHeaders(response, expectNoStore: false, "/not-an-api-path");
    }

    /// <summary>
    /// <c>Staging</c> (tin header chuyển tiếp, <c>X-Forwarded-Proto: https</c>, host không phải localhost) có <c>Strict-Transport-Security</c>;
    /// <c>Testing</c> và <c>Development</c> thì không dù request cũng là https.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-3")]
    [InlineData("Staging", true)]
    [InlineData("Testing", false)]
    [InlineData("Development", false)]
    public async Task Hsts_OnlyOutsideDevelopmentAndTesting_Theory(string environment, bool expectHsts)
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, environment, ("Tadss:Security:TrustForwardedHeaders", "true"));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.Host = "tadss.example";

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HstsHeader).Should().Be(expectHsts, environment);
    }

    /// <summary>
    /// HSTS (Staging, https) có cả trên response lỗi do exception handler ghi (400 body sai, 500 service ném): header gắn qua <c>OnStarting</c> nên
    /// sống sót qua <c>Response.Clear()</c>. Host <c>localhost</c> thuộc <c>ExcludedHosts</c> nên không có HSTS.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "SEC-3")]
    [InlineData("bad-body-400", "tadss.example", HttpStatusCode.BadRequest, true)]
    [InlineData("internal-500", "tadss.example", HttpStatusCode.InternalServerError, true)]
    [InlineData("ok-200", "tadss.example", HttpStatusCode.OK, true)]
    [InlineData("bad-body-400", "localhost", HttpStatusCode.BadRequest, false)]
    public async Task Hsts_PresentOnErrorResponses_Theory(string kind, string host, HttpStatusCode expectedStatus, bool expectHsts)
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, "Staging", ("Tadss:Security:TrustForwardedHeaders", "true"));
        if (kind == "internal-500")
        {
            _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("boom-hsts");
        }

        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        using var request = SecurityTestHost.Post(kind == "bad-body-400" ? "{" : ContractSamples.RequestJson, TestApiKeys.DefaultKey);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.Host = host;

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(expectedStatus, kind);
        response.Headers.Contains(HstsHeader).Should().Be(expectHsts, $"{kind} {host}");
    }

    /// <summary>Kestrel không gửi header <c>Server</c> (không lộ phần mềm máy chủ).</summary>
    [Fact]
    [Trait("CatalogId", "SEC-3")]
    public void Kestrel_ServerHeaderDisabled()
    {
        var options = _factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        options.AddServerHeader.Should().BeFalse();
    }
}
