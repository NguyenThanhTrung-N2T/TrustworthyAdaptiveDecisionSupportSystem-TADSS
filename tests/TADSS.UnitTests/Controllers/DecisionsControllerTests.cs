using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Mappings;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Middlewares;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Controllers;

/// <summary>
/// UT-02 / UT-08 / UT-11: <c>DecisionsController</c> chỉ chuyển tiếp (spec 4.2.3) — kiểm tra qua host in-process không DB/Redis,
/// <c>IDecisionService</c>/<c>IDecisionQueryService</c> là double. Mọi lỗi (header, body, domain) ra cùng một dạng RFC 7807 do
/// <c>GlobalExceptionHandler</c> dựng; controller không tự tạo <c>ProblemDetails</c>.
/// </summary>
public class DecisionsControllerTests : IClassFixture<DecisionsApiFactory>
{
    private const string DecisionsPath = "/api/v2/decisions";
    private const string ProblemContentType = "application/problem+json";

    private static readonly string[] ExtraMalformedBodyNames =
    [
        "broken-json",
        "empty-body",
        "json-null",
        "array-root",
        "channel-numeric",
        "total-price-as-string",
        "missing-reservation-id",
        "property-name-wrong-case"
    ];

    private readonly DecisionsApiFactory _factory;
    private readonly HttpClient _client;

    public DecisionsControllerTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
        _client = factory.CreateClient();
    }

    /// <summary>Toàn bộ ca âm của schema request cùng các ca riêng ở tầng HTTP (JSON hỏng, rỗng, enum số, sai kiểu, thiếu trường, sai hoa/thường).</summary>
    public static TheoryData<string> MalformedBodyCases
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in RequestSchemaNegativeCases.All)
            {
                data.Add((string)row[0]);
            }

            foreach (var name in ExtraMalformedBodyNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    private static string MalformedBody(string caseName) => caseName switch
    {
        "broken-json" => """{"reservationId": """,
        "empty-body" => string.Empty,
        "json-null" => "null",
        "array-root" => "[]",
        "channel-numeric" => V2Samples.MutateRequest(root => root["channel"] = 1),
        "total-price-as-string" => V2Samples.MutateRequest(root => root["totalPrice"] = "228000"),
        "missing-reservation-id" => V2Samples.MutateRequest(root => root.Remove("reservationId")),
        "property-name-wrong-case" => V2Samples.MutateRequest(root =>
        {
            var value = root["reservationId"]?.GetValue<string>();
            root.Remove("reservationId");
            root["ReservationId"] = value;
        }),
        _ => RequestSchemaNegativeCases.Json(caseName)
    };

    private async Task<HttpResponseMessage> PostAsync(
        string body,
        string? idempotencyKey = "booking-0001",
        string? correlationId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DecisionsPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        }

        return await _client.SendAsync(request);
    }

    private static string? MediaTypeOf(HttpResponseMessage response) => response.Content.Headers.ContentType?.MediaType;

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        MediaTypeOf(response).Should().Be(ProblemContentType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Body bằng đúng JSON mà <c>TadssJson</c> sinh ra cho <paramref name="expected"/>. So sánh theo cấu trúc (không theo byte) vì MVC
    /// dùng bộ mã hóa ký tự nới lỏng (giữ nguyên chữ có dấu) trong khi <c>TadssJson.Options</c> escape ký tự ngoài ASCII — cùng giá trị JSON.
    /// </summary>
    private static async Task AssertBodyEqualsTadssJsonAsync<T>(HttpResponseMessage response, T expected)
    {
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var expectedNode = JsonNode.Parse(JsonSerializer.Serialize(expected, TadssJson.Options));
        JsonNode.DeepEquals(actual, expectedNode).Should().BeTrue("body phải bằng JSON do TadssJson sinh ra cho cùng DTO");
    }

    /// <summary>Request hợp lệ + key được chuyển nguyên cho service cùng correlation id đã chuẩn hóa; body trả về đúng cấu hình <c>TadssJson</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task Post_ValidRequest_ForwardsToServiceAndReturns200()
    {
        var response = await PostAsync(V2Samples.RequestJson, "booking-0001", "corr-post-1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        MediaTypeOf(response).Should().Be("application/json");
        await AssertBodyEqualsTadssJsonAsync(response, FakeDecisionService.SampleResult());
        response.Headers.Contains("X-Decision-ID").Should().BeFalse("C-06: không có header X-Decision-ID");
        var call = _factory.DecisionService.Calls.Should().ContainSingle().Which;
        call.IdempotencyKey.Should().Be("booking-0001");
        call.CorrelationId.Should().Be("corr-post-1");
        call.Request.ReservationId.Should().Be(Guid.Parse("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11"));
    }

    /// <summary>Thiếu/rỗng/toàn khoảng trắng đều là 400 <c>MISSING_HEADER</c> và service không bị gọi.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_MissingOrBlankIdempotencyKey_Is400MissingHeader_Theory(string? key)
    {
        var response = await PostAsync(V2Samples.RequestJson, key);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
        problem.GetProperty("errors").GetProperty(HttpHeaderNames.IdempotencyKey).GetArrayLength().Should().Be(1);
        _factory.DecisionService.Calls.Should().BeEmpty();
    }

    /// <summary>Biên 128/129 ký tự: 129 là 400 <c>VALIDATION_ERROR</c> (trường là tên header), 128 là 200.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Post_IdempotencyKeyOver128Chars_Is400ValidationError_And128Succeeds()
    {
        var tooLong = await PostAsync(V2Samples.RequestJson, new string('k', 129));
        var atLimit = await PostAsync(V2Samples.RequestJson, new string('k', 128));

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(tooLong);
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        problem.GetProperty("errors").TryGetProperty(HttpHeaderNames.IdempotencyKey, out _).Should().BeTrue();
        atLimit.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.DecisionService.Calls.Should().ContainSingle().Which.IdempotencyKey.Should().HaveLength(128);
    }

    /// <summary>
    /// Mọi ca âm của schema (bị chặn khi giải mã body hoặc ở bước <c>ToDomain</c> của service — double chạy đúng bước đó) và các
    /// ca thô riêng đều ra cùng một dạng 400 <c>VALIDATION_ERROR</c> problem+json có <c>traceId</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-02")]
    [MemberData(nameof(MalformedBodyCases))]
    public async Task Post_MalformedBody_Is400ValidationError_Theory(string caseName)
    {
        _factory.DecisionService.Behavior = request =>
        {
            request.ToDomain();
            return Task.FromResult(FakeDecisionService.SampleResult());
        };

        var response = await PostAsync(MalformedBody(caseName), correlationId: "corr-malformed");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, caseName);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR", caseName);
        problem.GetProperty("status").GetInt32().Should().Be(400);
        problem.GetProperty("traceId").GetString().Should().Be("corr-malformed");
        problem.GetProperty("type").GetString().Should().Be("https://tadss.smashcourt.vn/errors/validation-error");
        problem.GetProperty("instance").GetString().Should().Be(DecisionsPath);
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        problem.EnumerateObject().Select(property => property.Name)
            .Should().BeSubsetOf(["type", "title", "status", "detail", "instance", "code", "traceId", "errors"], caseName);
    }

    /// <summary>Body hợp lệ nhưng service ném lỗi nghiệp vụ vẫn đi qua cùng handler (không có <c>ProblemDetails</c> riêng trong controller).</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Post_ServiceThrowsIdempotencyConflict_Is409ThroughGlobalHandler()
    {
        _factory.DecisionService.Behavior = _ => throw new IdempotencyConflictException("booking-0001", "Khóa idempotency đã dùng cho nội dung khác.");

        var response = await PostAsync(V2Samples.RequestJson);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSE");
    }

    /// <summary>GET trả chi tiết do service dựng, serialize bằng <c>TadssJson</c>, và truyền đúng <c>decisionId</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task Get_ExistingDecision_Returns200WithDetail()
    {
        var decisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");

        var response = await _client.GetAsync($"{DecisionsPath}/{decisionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertBodyEqualsTadssJsonAsync(response, FakeDecisionQueryService.SampleDetail());
        _factory.QueryService.RequestedIds.Should().ContainSingle().Which.Should().Be(decisionId);
    }

    /// <summary>Quyết định không tồn tại: 404 <c>DECISION_NOT_FOUND</c> qua handler.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Get_Unknown_Is404DecisionNotFound()
    {
        var decisionId = Guid.NewGuid();
        _factory.QueryService.Behavior = id => throw new DecisionNotFoundException(id);

        var response = await _client.GetAsync($"{DecisionsPath}/{decisionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        problem.GetProperty("detail").GetString().Should().Contain(decisionId.ToString());
    }

    /// <summary>
    /// Khóa hành vi route: <c>decisionId</c> không phải GUID không khớp ràng buộc <c>:guid</c> nên là 404 của routing
    /// (không thành 400), và service tra cứu không bị gọi.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Get_InvalidGuidRoute_Is404OrBadRequest()
    {
        var response = await _client.GetAsync($"{DecisionsPath}/abc");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.QueryService.RequestedIds.Should().BeEmpty();
    }
}
