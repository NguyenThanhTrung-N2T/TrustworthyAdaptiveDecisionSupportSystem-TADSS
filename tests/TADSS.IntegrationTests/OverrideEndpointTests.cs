using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.Enums;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-06 / IT-07 / IT-08 / IT-11: <c>POST /api/v2/decisions/{decisionId}/override</c> trên PostgreSQL + Redis thật qua pipeline HTTP thật, đồng hồ giả.
/// Mọi response 200 đã được kiểm theo <c>override-response.schema.json</c> (CT-01); chi tiết quyết định (<c>GET</c>) kiểm theo <c>decision-detail.schema.json</c>.
/// Tranh chấp override-override do <c>UNIQUE(decision_id)</c> bảo đảm nên đúng một hàng <c>decision_overrides</c>.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class OverrideEndpointTests : DecisionApiTestBase
{
    private const string Reason = "Khách quen, chi nhánh bảo lãnh";
    private const string ActorId = "manager-01";
    private const string ApproveRateBody = """{"action":"APPROVE","deposit":{"rate":0.3},"reason":"Khách quen, chi nhánh bảo lãnh"}""";
    private const string ApproveAmountBody = """{"action":"APPROVE","deposit":{"amount":100000},"reason":"Khách quen, chi nhánh bảo lãnh"}""";
    private const string RejectBody = """{"action":"REJECT","reason":"Không đủ sân cho khung giờ này"}""";
    private const string SampleBranchId = "1f2e3d4c-0000-4000-8000-000000000001";

    public OverrideEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>
    /// Duyệt theo <c>rate</c> hoặc theo <c>amount</c> (đơn 2.400.000 VND): response quy đổi đủ rate và amount, chi tiết quyết định có override,
    /// quyết định rời hàng chờ và có đúng một hàng <c>decision_overrides</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-06")]
    [InlineData(ApproveRateBody, 0.3, 720_000)]
    [InlineData(ApproveAmountBody, 0.0417, 100_000)]
    public async Task Override_Approve_ShowsInDetailAndLeavesQueue_Theory(string body, double expectedRate, double expectedAmount)
    {
        var pending = await CreatePendingDecisionAsync();
        var before = await GetPendingApprovalsAsync($"?branchId={SampleBranchId}");
        before.Pending.Items.Select(item => item.DecisionId).Should().Contain(pending.DecisionId);

        var response = await PostOverrideAsync(pending.DecisionId, body, NewKey(), ActorId);

        response.Status.Should().Be(HttpStatusCode.OK);
        var overridden = response.Override;
        overridden.DecisionId.Should().Be(pending.DecisionId);
        overridden.Action.Should().Be(OverrideAction.APPROVE);
        overridden.ActorId.Should().Be(ActorId);
        overridden.Reason.Should().Be(Reason);
        overridden.Deposit.Should().NotBeNull();
        overridden.Deposit?.Rate.Should().Be((decimal)expectedRate);
        overridden.Deposit?.Amount.Should().Be((decimal)expectedAmount);
        overridden.Deposit?.Currency.Should().Be("VND");
        overridden.OverriddenAt.Should().Be(InitialInstant);

        var detail = (await GetDecisionAsync(pending.DecisionId)).Detail;
        detail.Override.Should().BeEquivalentTo(overridden);
        detail.Result.Status.Should().Be(DecisionStatus.NEED_APPROVAL, "override không sửa bản ghi đánh giá (bất biến)");
        var after = await GetPendingApprovalsAsync($"?branchId={SampleBranchId}");
        after.Pending.Items.Select(item => item.DecisionId).Should().NotContain(pending.DecisionId);
        var rows = await ReadOverridesAsync();
        var row = rows.Should().ContainSingle().Which;
        row.DecisionId.Should().Be(pending.DecisionId);
        row.ActorId.Should().Be(ActorId);
        row.DepositAmount.Should().Be((decimal)expectedAmount);
    }

    /// <summary>Từ chối: 200 với <c>deposit = null</c>, hàng ghi không có cọc, chi tiết có override <c>REJECT</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-06")]
    public async Task Override_Reject_Succeeds()
    {
        var pending = await CreatePendingDecisionAsync();

        var response = await PostOverrideAsync(pending.DecisionId, RejectBody, NewKey(), ActorId);

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Override.Action.Should().Be(OverrideAction.REJECT);
        response.Override.Deposit.Should().BeNull();
        var detail = (await GetDecisionAsync(pending.DecisionId)).Detail;
        detail.Override?.Action.Should().Be(OverrideAction.REJECT);
        var row = (await ReadOverridesAsync()).Should().ContainSingle().Which;
        row.DepositRate.Should().BeNull();
        row.DepositAmount.Should().BeNull();
    }

    /// <summary>Gửi lại cùng key và cùng nội dung trả lại bản cũ; cùng key khác nội dung và khác key đều là 409 với mã đúng nghĩa.</summary>
    [Fact]
    [Trait("CatalogId", "IT-06")]
    public async Task Override_ReplayAndKeyConflicts_MapToContractCodes()
    {
        var pending = await CreatePendingDecisionAsync();
        var key = NewKey();
        var first = await PostOverrideAsync(pending.DecisionId, ApproveRateBody, key, ActorId);
        Clock.Advance(TimeSpan.FromMinutes(1));

        var replay = await PostOverrideAsync(pending.DecisionId, ApproveRateBody, key, ActorId);
        var sameKeyOtherPayload = await PostOverrideAsync(pending.DecisionId, RejectBody, key, ActorId);
        var otherKey = await PostOverrideAsync(pending.DecisionId, ApproveRateBody, NewKey(), ActorId);

        first.Status.Should().Be(HttpStatusCode.OK);
        replay.Status.Should().Be(HttpStatusCode.OK);
        replay.Override.Should().BeEquivalentTo(first.Override, "replay trả đúng bản đã lưu, không đóng dấu lại thời điểm");
        sameKeyOtherPayload.Status.Should().Be(HttpStatusCode.Conflict);
        sameKeyOtherPayload.Problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSE");
        otherKey.Status.Should().Be(HttpStatusCode.Conflict);
        otherKey.Problem.GetProperty("code").GetString().Should().Be("ALREADY_OVERRIDDEN");
        (await ReadOverridesAsync()).Should().ContainSingle();
    }

    /// <summary>
    /// Replay (F-03) trên nhánh <c>amount</c> và <c>REJECT</c>: cùng key + cùng nội dung → 200 với bản cũ (không đóng dấu lại thời điểm, vẫn một hàng);
    /// cùng key nhưng amount khác → 409 <c>IDEMPOTENCY_KEY_REUSE</c>.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-06")]
    [InlineData(ApproveAmountBody)]
    [InlineData(RejectBody)]
    public async Task Override_ReplayOnAmountAndRejectPaths_ReturnsStoredBody_Theory(string body)
    {
        var pending = await CreatePendingDecisionAsync();
        var key = NewKey();
        var first = await PostOverrideAsync(pending.DecisionId, body, key, ActorId);
        Clock.Advance(TimeSpan.FromMinutes(1));

        var replay = await PostOverrideAsync(pending.DecisionId, body, key, ActorId);
        var changed = await PostOverrideAsync(
            pending.DecisionId,
            body == RejectBody ? ApproveRateBody : """{"action":"APPROVE","deposit":{"amount":120000},"reason":"Khách quen, chi nhánh bảo lãnh"}""",
            key,
            ActorId);

        first.Status.Should().Be(HttpStatusCode.OK);
        replay.Status.Should().Be(HttpStatusCode.OK);
        replay.Override.Should().BeEquivalentTo(first.Override, "replay trả đúng bản đã lưu, không đóng dấu lại thời điểm");
        changed.Status.Should().Be(HttpStatusCode.Conflict);
        changed.Problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSE");
        (await ReadOverridesAsync()).Should().ContainSingle();
    }

    /// <summary>20 request song song với key và actor khác nhau: đúng một thắng (200), 19 còn lại <c>ALREADY_OVERRIDDEN</c>; chỉ một hàng.</summary>
    [Fact]
    [Trait("CatalogId", "IT-07")]
    public async Task Override_TwentyConcurrentDistinctKeys_OneWins()
    {
        var pending = await CreatePendingDecisionAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            Task.Run(() => PostOverrideAsync(pending.DecisionId, ApproveRateBody, NewKey(), $"manager-{index:00}", CreateApiClient()))));

        responses.Count(r => r.Status == HttpStatusCode.OK).Should().Be(1);
        var losers = responses.Where(r => r.Status != HttpStatusCode.OK).ToList();
        losers.Should().HaveCount(19).And.OnlyContain(r => r.Status == HttpStatusCode.Conflict);
        losers.Select(r => r.Problem.GetProperty("code").GetString()).Should().OnlyContain(code => code == "ALREADY_OVERRIDDEN");
        var row = (await ReadOverridesAsync()).Should().ContainSingle().Which;
        var winner = responses.Single(r => r.Status == HttpStatusCode.OK).Override;
        row.ActorId.Should().Be(winner.ActorId);
    }

    /// <summary>20 request song song giống hệt nhau (cùng key, actor, nội dung): cả 20 là 200 với cùng một override; chỉ một hàng.</summary>
    [Fact]
    [Trait("CatalogId", "IT-07")]
    public async Task Override_TwentyConcurrentSameKeyAndPayload_AllSucceedIdentically()
    {
        var pending = await CreatePendingDecisionAsync();
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => PostOverrideAsync(pending.DecisionId, ApproveRateBody, key, ActorId, CreateApiClient()))));

        responses.Should().OnlyContain(r => r.Status == HttpStatusCode.OK);
        var first = responses[0].Override;
        responses.Select(r => r.Override).Should().OnlyContain(item => EquivalentTo(item, first));
        (await ReadOverridesAsync()).Should().ContainSingle();
    }

    private static bool EquivalentTo(OverrideResponseDto left, OverrideResponseDto right) =>
        left.DecisionId == right.DecisionId
        && left.Action == right.Action
        && left.ActorId == right.ActorId
        && left.Reason == right.Reason
        && left.OverriddenAt == right.OverriddenAt
        && left.Deposit?.Rate == right.Deposit?.Rate
        && left.Deposit?.Amount == right.Deposit?.Amount
        && left.Deposit?.Currency == right.Deposit?.Currency;

    /// <summary>
    /// Quyết định <c>APPROVED</c> → <c>NOT_PENDING_APPROVAL</c>; <c>Clock</c> tiến quá <c>approvalExpiresAt</c> → <c>APPROVAL_EXPIRED</c>;
    /// đã có outcome → <c>NOT_PENDING_APPROVAL</c>. Mỗi lỗi là problem+json có <c>code</c> và <c>traceId</c>; không ghi hàng override.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-08")]
    [InlineData("Approved", "NOT_PENDING_APPROVAL")]
    [InlineData("Expired", "APPROVAL_EXPIRED")]
    [InlineData("HasOutcome", "NOT_PENDING_APPROVAL")]
    public async Task Override_NotPendingOrExpired_Returns409_Theory(string scenario, string expectedCode)
    {
        Guid decisionId;
        switch (scenario)
        {
            case "Approved":
                var approved = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());
                approved.Result.Status.Should().Be(DecisionStatus.APPROVED);
                decisionId = approved.Result.DecisionId;
                break;
            case "Expired":
                var expiring = await CreatePendingDecisionAsync();
                decisionId = expiring.DecisionId;
                Clock.Advance(expiring.ApprovalExpiresAt!.Value - Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
                break;
            default:
                var withOutcome = await CreatePendingDecisionAsync();
                decisionId = withOutcome.DecisionId;
                var outcome = await PostOutcomeAsync(
                    decisionId, """{"outcome":"APPROVAL_EXPIRED","occurredAt":"2026-11-18T10:00:00+07:00"}""", NewKey());
                outcome.Status.Should().Be(HttpStatusCode.OK);
                break;
        }

        var response = await PostOverrideAsync(decisionId, ApproveRateBody, NewKey(), ActorId);

        response.Status.Should().Be(HttpStatusCode.Conflict);
        response.ContentType.Should().Be("application/problem+json");
        response.Problem.GetProperty("code").GetString().Should().Be(expectedCode);
        response.Problem.GetProperty("status").GetInt32().Should().Be(409);
        response.Problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        (await ReadOverridesAsync()).Should().BeEmpty();
    }

    /// <summary>Thiếu <c>X-Actor-Id</c> → 400 <c>MISSING_HEADER</c> và không ghi gì.</summary>
    [Fact]
    [Trait("CatalogId", "IT-11")]
    public async Task Override_MissingActorId_Returns400MissingHeader()
    {
        var pending = await CreatePendingDecisionAsync();

        var response = await PostOverrideAsync(pending.DecisionId, ApproveRateBody, NewKey(), actorId: null);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
        response.Problem.GetProperty("errors").TryGetProperty("X-Actor-Id", out _).Should().BeTrue();
        (await ReadOverridesAsync()).Should().BeEmpty();
    }

    /// <summary>Định danh quyết định không có trong database → 404 <c>DECISION_NOT_FOUND</c>.</summary>
    [Fact]
    [Trait("CatalogId", "IT-08")]
    public async Task Override_UnknownDecision_Returns404()
    {
        var unknown = Guid.NewGuid();

        var response = await PostOverrideAsync(unknown, ApproveRateBody, NewKey(), ActorId);

        response.Status.Should().Be(HttpStatusCode.NotFound);
        response.ContentType.Should().Be("application/problem+json");
        response.Problem.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        response.Problem.GetProperty("detail").GetString().Should().Contain(unknown.ToString());
    }

    /// <summary>
    /// Số tiền cọc sai so với đơn được kiểm tra trước bước "đã override": request như vậy gửi vào quyết định đã được override vẫn là 400
    /// <c>VALIDATION_ERROR</c> (trường <c>deposit.amount</c>), không phải 409; quyết định không lạ thì vẫn 404 trước đó.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "IT-08")]
    [InlineData("""{"action":"APPROVE","deposit":{"amount":2400001},"reason":"Khách quen, chi nhánh bảo lãnh"}""")]
    [InlineData("""{"action":"APPROVE","deposit":{"amount":1000.5},"reason":"Khách quen, chi nhánh bảo lãnh"}""")]
    public async Task Override_MalformedAmountOnAlreadyOverriddenDecision_Returns400NotConflict_Theory(string body)
    {
        var pending = await CreatePendingDecisionAsync();
        (await PostOverrideAsync(pending.DecisionId, ApproveRateBody, NewKey(), ActorId)).Status.Should().Be(HttpStatusCode.OK);

        var response = await PostOverrideAsync(pending.DecisionId, body, NewKey(), ActorId);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        response.Problem.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");
        response.Problem.GetProperty("errors").TryGetProperty("deposit.amount", out _).Should().BeTrue();
        (await ReadOverridesAsync()).Should().ContainSingle();
    }
}
