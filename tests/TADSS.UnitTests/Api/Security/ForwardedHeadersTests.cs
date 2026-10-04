using System.Net;
using FluentAssertions;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-3 (F-10): <c>X-Forwarded-*</c> chỉ được tin khi <c>Tadss:Security:TrustForwardedHeaders = true</c> (mặc định tắt vì client có thể giả header).
/// Quan sát qua <c>Request.IsHttps</c>: HSTS (môi trường <c>Staging</c>) chỉ được gắn khi request được coi là https.
/// </summary>
public class ForwardedHeadersTests : IClassFixture<DecisionsApiFactory>
{
    private readonly DecisionsApiFactory _factory;

    public ForwardedHeadersTests(DecisionsApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Bật thì <c>X-Forwarded-Proto: https</c> làm request là https (có HSTS); tắt (mặc định) thì header bị bỏ qua (không HSTS).</summary>
    [Theory]
    [Trait("CatalogId", "SEC-3")]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task ForwardedHeaders_Theory(string trust, bool expectHttps)
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, "Staging", ("Tadss:Security:TrustForwardedHeaders", trust));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.Host = "tadss.example";

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").Should().Be(expectHttps);
    }

    /// <summary>Không cấu hình gì (mặc định) thì không tin header chuyển tiếp.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-3")]
    public async Task ForwardedHeaders_DefaultIsNotTrusted()
    {
        using var factory = SecurityTestHost.WithEnvironment(_factory, "Staging");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.Host = "tadss.example";

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").Should().BeFalse();
    }
}
