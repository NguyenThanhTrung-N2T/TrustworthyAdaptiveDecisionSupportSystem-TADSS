using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.Enums;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-10: <c>POST /api/v2/decisions/{decisionId}/outcome</c> trên PostgreSQL thật qua pipeline HTTP thật, đồng hồ giả. Response 200 đã được kiểm
/// theo <c>outcome-response.schema.json</c> (CT-01). <c>X-Idempotency-Key</c> bắt buộc nhưng không được lưu: chống trùng theo payload.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class OutcomeEndpointTests : DecisionApiTestBase
{
    private const string NoShowBody = """{"outcome":"NO_SHOW","occurredAt":"2026-11-20T18:30:00+07:00"}""";
    private const string CompletedBody = """{"outcome":"COMPLETED","occurredAt":"2026-11-20T18:30:00+07:00"}""";

    public OutcomeEndpointTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>
    /// Ghi outcome → 200; gửi lại y hệt (kể cả key khác, sau khi đồng hồ tiến) → 200 cùng <c>reportedAt</c>; gửi nội dung khác → 409
    /// <c>OUTCOME_ALREADY_RECORDED</c>; chi tiết quyết định hiện outcome; chỉ một hàng <c>decision_outcomes</c>.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task Outcome_RecordReplayConflict()
    {
        var posted = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());
        var decisionId = posted.Result.DecisionId;

        var first = await PostOutcomeAsync(decisionId, NoShowBody, NewKey());
        Clock.Advance(TimeSpan.FromMinutes(5));
        var replay = await PostOutcomeAsync(decisionId, NoShowBody, NewKey());
        var conflict = await PostOutcomeAsync(decisionId, CompletedBody, NewKey());

        first.Status.Should().Be(HttpStatusCode.OK);
        first.Outcome.DecisionId.Should().Be(decisionId);
        first.Outcome.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
        first.Outcome.OccurredAt.Should().Be(new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.Zero));
        first.Outcome.OccurredAt.Offset.Should().Be(TimeSpan.Zero, "thời điểm trong response trả theo UTC");
        first.Outcome.ReportedAt.Should().Be(InitialInstant);
        replay.Status.Should().Be(HttpStatusCode.OK);
        replay.Outcome.ReportedAt.Should().Be(first.Outcome.ReportedAt, "gửi lại trả đúng bản đã lưu, không ghi nhận lại");
        replay.Outcome.Should().BeEquivalentTo(first.Outcome);
        conflict.Status.Should().Be(HttpStatusCode.Conflict);
        conflict.ContentType.Should().Be("application/problem+json");
        conflict.Problem.GetProperty("code").GetString().Should().Be("OUTCOME_ALREADY_RECORDED");
        conflict.Problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();

        var detail = (await GetDecisionAsync(decisionId)).Detail;
        detail.Outcome.Should().BeEquivalentTo(first.Outcome);
        var row = (await ReadOutcomesAsync()).Should().ContainSingle().Which;
        row.Outcome.Should().Be(DecisionOutcome.NO_SHOW);
    }

    /// <summary>Outcome áp cho quyết định ở mọi trạng thái, kể cả <c>NEED_APPROVAL</c> chưa được duyệt.</summary>
    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task Outcome_PendingDecision_IsAccepted()
    {
        var pending = await CreatePendingDecisionAsync();

        var response = await PostOutcomeAsync(pending.DecisionId, NoShowBody, NewKey());

        response.Status.Should().Be(HttpStatusCode.OK);
        (await ReadOutcomesAsync()).Should().ContainSingle();
    }

    /// <summary>Thiếu <c>X-Idempotency-Key</c> → 400 <c>MISSING_HEADER</c>; quyết định lạ → 404 <c>DECISION_NOT_FOUND</c>; không ghi gì.</summary>
    [Fact]
    [Trait("CatalogId", "IT-10")]
    public async Task Outcome_MissingKeyAndUnknownDecision_Return400And404()
    {
        var posted = await PostDecisionAsync(DecisionRequests.Valid(), NewKey());

        var missingKey = await PostOutcomeAsync(posted.Result.DecisionId, NoShowBody, idempotencyKey: null);
        var unknown = await PostOutcomeAsync(Guid.NewGuid(), NoShowBody, NewKey());

        missingKey.Status.Should().Be(HttpStatusCode.BadRequest);
        missingKey.Problem.GetProperty("code").GetString().Should().Be("MISSING_HEADER");
        unknown.Status.Should().Be(HttpStatusCode.NotFound);
        unknown.Problem.GetProperty("code").GetString().Should().Be("DECISION_NOT_FOUND");
        (await ReadOutcomesAsync()).Should().BeEmpty();
    }
}
