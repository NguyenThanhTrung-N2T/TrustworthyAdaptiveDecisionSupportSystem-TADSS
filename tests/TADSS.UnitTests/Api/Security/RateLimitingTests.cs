using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.OK);
        (await StatusOf(client, PendingPath, TestApiKeys.DefaultKey)).Should().Be(HttpStatusCode.OK);
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

    /// <summary>Năm request không có khóa với trần 2 request: đều 401, không có 429 nào, chứng minh xác thực đứng trước rate limiter (nếu đảo thứ tự thì request thứ ba đã bị 429).</summary>
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

    private static SecurityOptionsValidator CreateValidator(string environmentName) =>
        new(Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == environmentName));

    /// <summary>
    /// <c>ClientId</c> trùng khóa phân vùng của rate limiter (<c>health</c>, <c>anonymous</c>, không phân biệt hoa thường) bị từ chối ở mọi môi trường;
    /// thông báo nêu chỉ số phần tử và không chứa mã băm; <c>ClientId</c> chỉ chứa tên đó như một phần (<c>health-check</c>) thì hợp lệ.
    /// </summary>
    [Theory]
    [InlineData("health", false)]
    [InlineData("HEALTH", false)]
    [InlineData("anonymous", false)]
    [InlineData("Anonymous", false)]
    [InlineData("health-check", true)]
    [InlineData("anonymous1", true)]
    [Trait("CatalogId", "SEC-1")]
    public void Validate_ReservedClientId_Theory(string clientId, bool expectedValid)
    {
        var hash = TestApiKeys.Sha256Hex("rate-limit-reserved-key");
        var options = new SecurityOptions
        {
            ApiKeys =
            [
                new ApiKeyClientOptions { ClientId = "be", KeySha256 = TestApiKeys.Sha256Hex("rate-limit-first-key") },
                new ApiKeyClientOptions { ClientId = clientId, KeySha256 = hash }
            ]
        };

        foreach (var environmentName in new[] { "Testing", "Production" })
        {
            var result = CreateValidator(environmentName).Validate(null, options);

            result.Succeeded.Should().Be(expectedValid, environmentName);
            if (!expectedValid)
            {
                result.FailureMessage.Should().Contain("Tadss:Security:ApiKeys:1:ClientId");
                result.FailureMessage.Should().NotContainEquivalentOf(hash);
            }
        }
    }

    /// <summary>Section <c>RateLimiting</c> bị gán <c>null</c>: validator không ném mà báo lỗi rõ ràng nêu tên section (cùng cách xử lý phần tử <c>ApiKeys</c> null).</summary>
    [Fact]
    [Trait("CatalogId", "SEC-1")]
    public void Validate_NullRateLimitingSection_FailsClearly()
    {
        // Thuộc tính không nullable: gán null bằng reflection như khi cấu hình nguồn ép section về null
        var options = new SecurityOptions();
        typeof(SecurityOptions).GetProperty(nameof(SecurityOptions.RateLimiting))?.SetValue(options, null);

        var result = CreateValidator("Testing").Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Tadss:Security:RateLimiting");
    }

    /// <summary>
    /// Callback <c>OnRejected</c> trực tiếp: không có metadata <c>RetryAfter</c> thì <c>Retry-After</c> = chu kỳ nạp cấu hình (3600);
    /// metadata 0,2 giây làm tròn lên thành tối thiểu 1; 1,2 giây thành 2. Header và <c>retryAfterSeconds</c> của body luôn khớp.
    /// </summary>
    [Theory]
    [InlineData(null, 3600)]
    [InlineData(0.0, 1)]
    [InlineData(0.2, 1)]
    [InlineData(1.0, 1)]
    [InlineData(1.2, 2)]
    [Trait("CatalogId", "SEC-1")]
    public async Task OnRejected_RetryAfter_FallsBackAndHasMinimumOne(double? metadataSeconds, int expectedSeconds)
    {
        using var baseFactory = CreateBaseFactory();
        using var factory = WithTightLimit(baseFactory);
        var onRejected = factory.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected
            ?? throw new InvalidOperationException("OnRejected chưa được cấu hình.");
        var body = new MemoryStream();
        var httpContext = new DefaultHttpContext { RequestServices = factory.Services };
        httpContext.Response.Body = body;
        var lease = new FakeLease(metadataSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        await onRejected(new OnRejectedContext { HttpContext = httpContext, Lease = lease }, CancellationToken.None);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be(expectedSeconds.ToString(CultureInfo.InvariantCulture));
        using var problem = JsonDocument.Parse(body.ToArray());
        problem.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMITED");
        problem.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(expectedSeconds);
    }

    /// <summary>Lease bị từ chối giả: chỉ có metadata <c>RetryAfter</c> khi được cấp.</summary>
    private sealed class FakeLease(TimeSpan? retryAfter) : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = metadataName == MetadataName.RetryAfter.Name ? retryAfter : null;
            return metadata is not null;
        }
    }
}
