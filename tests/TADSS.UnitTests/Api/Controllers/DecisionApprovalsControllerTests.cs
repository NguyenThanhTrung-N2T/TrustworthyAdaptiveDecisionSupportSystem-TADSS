using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Common;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Controllers;

/// <summary>
/// UT-08 / UT-11: <c>DecisionApprovalsController</c> chỉ chuyển tiếp (spec 4.2.3) — kiểm tra qua host in-process không DB/Redis,
/// <c>IOverrideService</c>/<c>IDecisionQueryService</c> là double. Header <c>X-Idempotency-Key</c>/<c>X-Actor-Id</c> (độ dài đếm theo code point),
/// tham số truy vấn của hàng chờ, và mọi lỗi (header, body, nghiệp vụ) ra cùng một dạng RFC 7807 do <c>GlobalExceptionHandler</c> dựng.
/// </summary>
public class DecisionApprovalsControllerTests : IClassFixture<DecisionsApiFactory>
{
    private const string BasePath = "/api/v2/decisions";
    private const string PendingPath = BasePath + "/pending-approvals";
    private const string ProblemContentType = "application/problem+json";
    private const string ValidBody = """{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Khách quen, chi nhánh bảo lãnh"}""";

    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d001");
    private static readonly Guid BranchId = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001");

    private readonly DecisionsApiFactory _factory;
    private readonly HttpClient _client;

    public DecisionApprovalsControllerTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> PostOverrideAsync(
        string body,
        string? idempotencyKey = "override-key-0001",
        string? actorId = "manager-01",
        string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BasePath}/{DecisionId}/override")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, idempotencyKey);
        }

        if (actorId is not null)
        {
            request.Headers.TryAddWithoutValidation(HttpHeaderNames.ActorId, actorId);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be(ProblemContentType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Override hợp lệ: chuyển nguyên decisionId, request, actor và key cho service; body bằng JSON do <c>TadssJson</c> sinh ra.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task Override_ValidRequest_ForwardsToServiceAndReturns200()
    {
        var response = await PostOverrideAsync(ValidBody);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        var expected = JsonNode.Parse(JsonSerializer.Serialize(FakeOverrideService.SampleResponse(DecisionId), TadssJson.Options));
        JsonNode.DeepEquals(actual, expected).Should().BeTrue();
        var call = _factory.OverrideService.Calls.Should().ContainSingle().Which;
        call.DecisionId.Should().Be(DecisionId);
        call.ActorId.Should().Be("manager-01");
        call.IdempotencyKey.Should().Be("override-key-0001");
        call.Request.Action.Should().Be(OverrideAction.APPROVE);
        call.Request.Deposit?.Rate.Should().Be(0.3m);
        call.Request.Reason.Should().Be("Khách quen, chi nhánh bảo lãnh");
    }

    /// <summary>Thiếu hoặc trắng <c>X-Actor-Id</c> / <c>X-Idempotency-Key</c> → 400 <c>MISSING_HEADER</c> (trường là tên header) và service không bị gọi.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(null, "manager-01", HttpHeaderNames.IdempotencyKey)]
    [InlineData("   ", "manager-01", HttpHeaderNames.IdempotencyKey)]
    [InlineData("override-key-0001", null, HttpHeaderNames.ActorId)]
    [InlineData("override-key-0001", "   ", HttpHeaderNames.ActorId)]
    public async Task Override_MissingOrBlankHeader_Is400MissingHeader_Theory(string? key, string? actor, string headerName)
    {
        var response = await PostOverrideAsync(ValidBody, key, actor);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
        problem.GetProperty("errors").GetProperty(headerName).GetArrayLength().Should().Be(1);
        _factory.OverrideService.Calls.Should().BeEmpty();
    }

    /// <summary>Biên độ dài: key 129 và actor 101 ký tự → 400 <c>VALIDATION_ERROR</c>; key 128 và actor 100 ký tự → 200.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Override_HeaderLengthBoundaries_Are400AtOneOverLimitAnd200AtLimit()
    {
        var keyTooLong = await PostOverrideAsync(ValidBody, idempotencyKey: new string('k', ContractLimits.MaxIdempotencyKeyLength + 1));
        var actorTooLong = await PostOverrideAsync(ValidBody, actorId: new string('a', ContractLimits.MaxActorIdLength + 1));
        var calledSoFar = _factory.OverrideService.Calls.Count;
        var atLimit = await PostOverrideAsync(
            ValidBody, idempotencyKey: new string('k', ContractLimits.MaxIdempotencyKeyLength), actorId: new string('a', ContractLimits.MaxActorIdLength));

        keyTooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(keyTooLong)).GetProperty("errors").TryGetProperty(HttpHeaderNames.IdempotencyKey, out _).Should().BeTrue();
        actorTooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(actorTooLong);
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        problem.GetProperty("errors").TryGetProperty(HttpHeaderNames.ActorId, out _).Should().BeTrue();
        calledSoFar.Should().Be(0);
        atLimit.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.OverrideService.Calls.Should().ContainSingle();
    }

    /// <summary>Độ dài <c>X-Actor-Id</c> đếm theo Unicode code point (F-17): 100 ký tự ngoài BMP (200 đơn vị UTF-16) vẫn hợp lệ, 101 thì không.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(100, HttpStatusCode.OK)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    public async Task Override_ActorIdLengthCountedInCodePoints_Theory(int codePoints, HttpStatusCode expected)
    {
        var actor = string.Concat(Enumerable.Repeat("\U0001F600", codePoints));

        var response = await PostOverrideAsync(ValidBody, actorId: actor);

        response.StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK)
        {
            _factory.OverrideService.Calls.Should().ContainSingle().Which.ActorId.Should().Be(actor);
        }
    }

    /// <summary>Body sai hình thức (JSON hỏng, trường lạ, enum chữ thường/số, thiếu trường) → 400 <c>VALIDATION_ERROR</c> và service không bị gọi.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("""{"action": """)]
    [InlineData("")]
    [InlineData("""{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Lý do hợp lệ","extra":1}""")]
    [InlineData("""{"action":"approve","deposit":{"rate":0.3},"reason":"Lý do hợp lệ"}""")]
    [InlineData("""{"action":0,"deposit":{"rate":0.3},"reason":"Lý do hợp lệ"}""")]
    [InlineData("""{"deposit":{"rate":0.3},"reason":"Lý do hợp lệ"}""")]
    [InlineData("""{"action":"REJECT"}""")]
    [InlineData("""{"action":"APPROVE","deposit":{"rate":"0.3"},"reason":"Lý do hợp lệ"}""")]
    public async Task Override_MalformedBody_Is400ValidationError_Theory(string body)
    {
        var response = await PostOverrideAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        _factory.OverrideService.Calls.Should().BeEmpty();
    }

    /// <summary>Content-Type không phải JSON giữ mặc định của MVC (415) và service không bị gọi.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Override_NonJsonContentType_Is415()
    {
        var response = await PostOverrideAsync(ValidBody, contentType: "text/plain");

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
        _factory.OverrideService.Calls.Should().BeEmpty();
    }

    /// <summary>Lỗi nghiệp vụ của service đi qua <c>GlobalExceptionHandler</c>: mỗi mã 409/404/503 đúng status, <c>code</c> và có <c>traceId</c>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("ALREADY_OVERRIDDEN", 409)]
    [InlineData("NOT_PENDING_APPROVAL", 409)]
    [InlineData("APPROVAL_EXPIRED", 409)]
    [InlineData("IDEMPOTENCY_KEY_REUSE", 409)]
    [InlineData("DECISION_NOT_FOUND", 404)]
    [InlineData("PERSISTENCE_UNAVAILABLE", 503)]
    public async Task Override_ServiceErrors_MapThroughGlobalHandler_Theory(string code, int status)
    {
        _factory.OverrideService.Behavior = call => throw code switch
        {
            "ALREADY_OVERRIDDEN" => DecisionStateConflictException.AlreadyOverridden(call.DecisionId),
            "NOT_PENDING_APPROVAL" => DecisionStateConflictException.NotPendingApproval(call.DecisionId),
            "APPROVAL_EXPIRED" => DecisionStateConflictException.ApprovalExpired(call.DecisionId),
            "IDEMPOTENCY_KEY_REUSE" => new IdempotencyConflictException(call.IdempotencyKey, "Khóa idempotency đã dùng cho nội dung khác."),
            "DECISION_NOT_FOUND" => new DecisionNotFoundException(call.DecisionId),
            _ => new PersistenceUnavailableException("Không ghi được.", new InvalidOperationException("boom")),
        };

        var response = await PostOverrideAsync(ValidBody);

        ((int)response.StatusCode).Should().Be(status);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be(code);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Hàng chờ: truyền branchId và mặc định phân trang theo <c>ContractLimits</c> cho service; body đúng cấu hình <c>TadssJson</c>.</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task PendingApprovals_ValidQuery_ForwardsWithDefaultsAndReturns200()
    {
        (Guid BranchId, int Page, int PageSize)? seen = null;
        _factory.QueryService.PendingBehavior = (branchId, page, pageSize) =>
        {
            seen = (branchId, page, pageSize);
            return Task.FromResult(PagedResult<PendingApprovalItemDto>.Create([], page, pageSize, 0));
        };

        var response = await _client.GetAsync($"{PendingPath}?branchId={BranchId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        seen.Should().Be((BranchId, 1, ContractLimits.DefaultPageSize));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(ContractLimits.DefaultPageSize);
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>Giá trị phân trang tường minh được truyền nguyên cho service (kiểm miền giá trị là việc của service).</summary>
    [Fact]
    [Trait("CatalogId", "UT-11")]
    public async Task PendingApprovals_ExplicitPaging_IsForwardedAsIs()
    {
        (int Page, int PageSize)? seen = null;
        _factory.QueryService.PendingBehavior = (_, page, pageSize) =>
        {
            seen = (page, pageSize);
            return Task.FromResult(PagedResult<PendingApprovalItemDto>.Create([], page, pageSize, 0));
        };

        var response = await _client.GetAsync($"{PendingPath}?branchId={BranchId}&page=3&pageSize=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        seen.Should().Be((3, 7));
    }

    /// <summary>Thiếu hoặc sai kiểu <c>branchId</c>/<c>page</c>/<c>pageSize</c> → 400 <c>VALIDATION_ERROR</c>; service không bị gọi.</summary>
    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData("")]
    [InlineData("?branchId=not-a-guid")]
    [InlineData("?branchId=1f2e3d4c-0000-4000-8000-000000000001&page=abc")]
    [InlineData("?branchId=1f2e3d4c-0000-4000-8000-000000000001&pageSize=abc")]
    [InlineData("?branchId=1f2e3d4c-0000-4000-8000-000000000001&page=1.5")]
    public async Task PendingApprovals_MissingOrUnparsableQuery_Is400ValidationError_Theory(string query)
    {
        var called = false;
        _factory.QueryService.PendingBehavior = (_, page, pageSize) =>
        {
            called = true;
            return Task.FromResult(PagedResult<PendingApprovalItemDto>.Create([], page, pageSize, 0));
        };

        var response = await _client.GetAsync(PendingPath + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        called.Should().BeFalse();
    }

    /// <summary>Lỗi miền phân trang do service ném (<c>VALIDATION_ERROR</c>) ra 400 problem+json qua handler chung.</summary>
    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task PendingApprovals_ServiceRejectsPaging_Is400ThroughGlobalHandler()
    {
        _factory.QueryService.PendingBehavior = (_, _, _) =>
            throw new ContractValidationException(ErrorCodes.ValidationError, "pageSize phải trong 1..100.", "pageSize");

        var response = await _client.GetAsync($"{PendingPath}?branchId={BranchId}&pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        problem.GetProperty("errors").TryGetProperty("pageSize", out _).Should().BeTrue();
    }
}
