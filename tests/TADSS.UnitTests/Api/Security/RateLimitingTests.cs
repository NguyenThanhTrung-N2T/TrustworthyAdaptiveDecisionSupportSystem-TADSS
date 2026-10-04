using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Moq;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Options;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Security;

/// <summary>
/// SEC-1: giới hạn tốc độ theo client (token bucket). Host in-process với <c>PermitLimit = 2</c>, <c>TokensPerPeriod = 2</c> và chu kỳ nạp 3600 giây
/// (không nạp lại trong thời gian test): request thứ ba của cùng client bị 429 <c>RATE_LIMITED</c>, các client không ảnh hưởng nhau, health không bị giới hạn,
/// request thiếu khóa nhận 401 chứ không phải 429 và validator chặn tham số ngoài biên.
/// </summary>
public class RateLimitingTests
{
    private const string PendingPath = "/api/v2/decisions/pending-approvals?branchId=1f2e3d4c-0000-4000-8000-000000000001";
    private const string CorrelationId = "corr-rate-1";

    private static DecisionsApiFactory CreateBaseFactory() => new() { ApiKeys = [TestApiKeys.Default, TestApiKeys.Second] };

    private static WebApplicationFactory<Program> WithTightLimit(DecisionsApiFactory baseFactory) =>
        SecurityTestHost.WithEnvironment(
            baseFactory,
            DecisionsApiFactory.EnvironmentName,
            ("Tadss:Security:RateLimiting:PermitLimit", "2"),
            ("Tadss:Security:RateLimiting:TokensPerPeriod", "2"),
            ("Tadss:Security:RateLimiting:ReplenishmentPeriodSeconds", "3600"));

    private static HttpRequestMessage Get(string path, string? apiKey, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.ApiKey, apiKey);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        }

        return request;
    }

    private static async Task<HttpStatusCode> StatusOf(HttpClient client, string path, string? apiKey)
    {
        using var request = Get(path, apiKey);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>
    /// Ba request cùng client: 200, 200, rồi 429 dạng problem+json có <c>code = RATE_LIMITED</c>, <c>retryAfterSeconds</c> khớp header <c>Retry-After</c> (≥ 1),
    /// <c>traceId</c> = correlation id và đủ header bảo mật.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-1")]
    public async Task ExceedingLimit_Returns429Problem()
    {
        using var baseFactory = CreateBaseFactory();
        using var factory = WithTightLimit(baseFactory);
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.OK);
        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.OK);
        using var third = Get(PendingPath, TestApiKeys.DefaultKey, CorrelationId);
        using var response = await client.SendAsync(third);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var retryAfterSeconds = (int?)response.Headers.RetryAfter?.Delta?.TotalSeconds;
        retryAfterSeconds.Should().BeGreaterThanOrEqualTo(1, "429 phải có Retry-After dạng số giây");
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle().Which.Should().Be("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        problem.RootElement.GetProperty("traceId").GetString().Should().Be(CorrelationId);
        problem.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(retryAfterSeconds);
    }

    /// <summary>Client A dùng hết quota (request thứ ba bị 429); client B vẫn nhận 200 vì mỗi <c>ClientId</c> có xô riêng.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-1")]
    public async Task Clients_AreIsolated()
    {
        using var baseFactory = CreateBaseFactory();
        using var factory = WithTightLimit(baseFactory);
        using var client = TestApiKeys.CreateClientWithoutKey(factory);
        await StatusOf(client, PendingPath, TestApiKeys.DefaultKey);
        await StatusOf(client, PendingPath, TestApiKeys.DefaultKey);
        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.TooManyRequests);

        (await StatusOf(client, PendingPath, TestApiKeys.SecondKey)).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Mười lần <c>/health/live</c> (không khóa) với trần 2 request: không lần nào bị 429.</summary>
    [Fact]
    [Trait("CatalogId", "SEC-1")]
    public async Task Health_IsNeverLimited()
    {
        using var baseFactory = CreateBaseFactory();
        using var factory = WithTightLimit(baseFactory);
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        for (var i = 0; i < 10; i++)
        {
            (await StatusOf(client, "/health/live", apiKey: null)).Should().NotBe(HttpStatusCode.TooManyRequests, $"lần {i + 1}");
        }
    }

    /// <summary>Năm request không có khóa: đều 401 (xác thực đứng trước rate limiter) và không tốn lượt của client hợp lệ.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public async Task Unauthenticated_Gets401Not429()
    {
        using var baseFactory = CreateBaseFactory();
        using var factory = WithTightLimit(baseFactory);
        using var client = TestApiKeys.CreateClientWithoutKey(factory);

        for (var i = 0; i < 5; i++)
        {
            (await StatusOf(client, PendingPath, apiKey: null)).Should().Be(HttpStatusCode.Unauthorized, $"lần {i + 1}");
        }

        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// <c>PermitLimit</c> 1..100000, <c>TokensPerPeriod</c> 1..PermitLimit, <c>ReplenishmentPeriodSeconds</c> 1..3600: biên trong hợp lệ, ngay ngoài biên không hợp lệ;
    /// thông báo nêu tên khóa sai (PermitLimit = 0 làm sai cả TokensPerPeriod nên không đòi duy nhất một lỗi).
    /// </summary>
    [Theory]
    [InlineData(100, 100, 1, true, "")]
    [InlineData(1, 1, 1, true, "")]
    [InlineData(100000, 100000, 3600, true, "")]
    [InlineData(0, 1, 1, false, "PermitLimit")]
    [InlineData(100001, 100, 1, false, "PermitLimit")]
    [InlineData(100, 0, 1, false, "TokensPerPeriod")]
    [InlineData(100, 101, 1, false, "TokensPerPeriod")]
    [InlineData(100, 100, 0, false, "ReplenishmentPeriodSeconds")]
    [InlineData(100, 100, 3601, false, "ReplenishmentPeriodSeconds")]
    [Trait("CatalogId", "SEC-1")]
    public void Validate_RateLimiting_Theory(int permitLimit, int tokensPerPeriod, int replenishmentPeriodSeconds, bool expectedValid, string failingField)
    {
        var options = new SecurityOptions
        {
            RateLimiting = new RateLimitingOptions
            {
                PermitLimit = permitLimit,
                TokensPerPeriod = tokensPerPeriod,
                ReplenishmentPeriodSeconds = replenishmentPeriodSeconds
            }
        };
        var validator = new SecurityOptionsValidator(Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == "Testing"));

        var result = validator.Validate(null, options);

        result.Succeeded.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.Failures.Should().Contain(failure => failure.Contains(failingField), "thông báo phải nêu tên khóa sai");
        }
    }
}
