using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Controllers;

/// <summary>
/// UT-08 / UT-11: <c>DecisionOutcomesController</c> chỉ chuyển tiếp (spec 4.2.3) — kiểm tra qua host in-process không DB/Redis,
/// <c>IOutcomeService</c> là double. <c>X-Idempotency-Key</c> bắt buộc (kiểm theo §3.2, không chuyển cho service) và mọi lỗi ra
/// cùng một dạng RFC 7807 do <c>GlobalExceptionHandler</c> dựng.
/// </summary>
public class DecisionOutcomesControllerTests : IClassFixture<DecisionsApiFactory>
{
    private const string BasePath = "/api/v2/decisions";
    private const string ProblemContentType = "application/problem+json";
    private const string ValidBody = """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00"}""";

    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");

    private readonly DecisionsApiFactory _factory;
    private readonly HttpClient _client;

    public DecisionOutcomesControllerTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> PostOutcomeAsync(
        string body, string? idempotencyKey = "outcome-key-0001", string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{DecisionId}/outcome")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemContentType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Outcome hợp lệ: chuyển decisionId và request cho service; body bằng JSON do <c>TadssJson</c> sinh ra.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task Outcome_ValidRequest_ForwardsToServiceAndReturns200()
    {
        var response = await PostOutcomeAsync(ValidBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var expected = JsonNode.Parse(JsonSerializer.Serialize(FakeOutcomeService.SampleResponse(DecisionId), TadssJson.Options));
        JsonNode.DeepEquals(actual, expected).Should().BeTrue();
        var call = _factory.OutcomeService.Calls.Should().ContainSingle().Which;
        call.DecisionId.Should().Be(DecisionId);
        call.Request.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
        call.Request.OccurredAt.Should().Be(new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.FromHours(7)));
    }

    /// <summary>Thiếu/trắng <c>X-Idempotency-Key</c> → 400 <c>MISSING_HEADER</c>; dài hơn 128 → <c>VALIDATION_ERROR</c>; đúng 128 → 200.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Outcome_IdempotencyKeyRules_MissingBlankTooLongAndAtLimit()
    {
        var missing = await PostOutcomeAsync(ValidBody, idempotencyKey: null);
        var blank = await PostOutcomeAsync(ValidBody, idempotencyKey: "   ");
        var tooLong = await PostOutcomeAsync(ValidBody, idempotencyKey: new string('k', ContractLimits.MaxIdempotencyKeyLength + 1));
        var calledSoFar = _factory.OutcomeService.Calls.Count;
        var atLimit = await PostOutcomeAsync(ValidBody, idempotencyKey: new string('k', ContractLimits.MaxIdempotencyKeyLength));

        foreach (var response in new[] { missing, blank })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await ReadProblemAsync(response);
            problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
            problem.GetProperty("errors").TryGetProperty(HttpHeaderNames.IdempotencyKey, out _).Should().BeTrue();
        }

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(tooLong)).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        calledSoFar.Should().Be(0);
        atLimit.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.OutcomeService.Calls.Should().ContainSingle();
    }

    /// <summary>Body sai hình thức (JSON hỏng, trường lạ, enum sai, thời gian thiếu offset, thiếu trường) → 400 <c>VALIDATION_ERROR</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("""{"outcome": """)]
    [InlineData("")]
    [InlineData("""{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00","extra":1}""")]
    [InlineData("""{"outcome":"no_show","occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData("""{"outcome":1,"occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    [InlineData("""{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00"}""")]
    [InlineData("""{"outcome":"NO_SHOW"}""")]
    [InlineData("""{"occurredAt":"2026-11-20T18:30:00+07:00"}""")]
    public async Task Outcome_MalformedBody_Is400ValidationError_Theory(string body)
    {
        var response = await PostOutcomeAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        _factory.OutcomeService.Calls.Should().BeEmpty();
    }

    /// <summary>Content-Type không phải JSON giữ mặc định của MVC (415).</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Outcome_NonJsonContentType_Is415()
    {
        var response = await PostOutcomeAsync(ValidBody, contentType: "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _factory.OutcomeService.Calls.Should().BeEmpty();
    }

    /// <summary>Lỗi nghiệp vụ của service đi qua <c>GlobalExceptionHandler</c> (404, 409 <c>OUTCOME_ALREADY_RECORDED</c>, 503).</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("DECISION_NOT_FOUND", 404)]
    [InlineData("OUTCOME_ALREADY_RECORDED", 409)]
    [InlineData("PERSISTENCE_UNAVAILABLE", 503)]
    public async Task Outcome_ServiceErrors_MapThroughGlobalHandler_Theory(string code, int status)
    {
        _factory.OutcomeService.Behavior = call => throw code switch
        {
            "DECISION_NOT_FOUND" => new DecisionNotFoundException(call.DecisionId),
            "OUTCOME_ALREADY_RECORDED" => DecisionStateConflictException.OutcomeAlreadyRecorded(call.DecisionId),
            _ => new PersistenceUnavailableException("Không ghi được.", new InvalidOperationException("boom")),
        };

        var response = await PostOutcomeAsync(ValidBody);

        ((int)response.StatusCode).Should().Be(status);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
