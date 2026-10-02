using System.Net;
using FluentAssertions;
using Shared.DecisionContractSDK.Decisions;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.IntegrationTests;

/// <summary>
/// IT-02/IT-03/IT-04: idempotency của <c>POST /api/v2/decisions</c> qua HTTP thật trên PostgreSQL thật — gửi lại cùng key và cùng nội dung trả
/// đúng response đã lưu (giống từng byte) mà không chạy lại strategy, đổi nội dung nghiệp vụ thì <c>409 IDEMPOTENCY_KEY_REUSE</c>,
/// và 50 request song song cùng key chỉ để lại một hàng audit.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class IdempotencyIntegrationTests : DecisionApiTestBase
{
    private const int Concurrency = 50;

    public IdempotencyIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>Hai lần cùng key và payload: body giống từng byte, một hàng, strategy chạy đúng một lần (đồng hồ đã tiến nên replay không dựng lại).</summary>
    [Fact]
    [Trait("CatalogId", "IT-02")]
    public async Task PostDecision_SameKeySamePayload_ReplaysStoredResponseWithoutRerunningStrategy()
    {
        // Tắt cache của strategy để "strategy chạy một lần" chỉ có thể do replay (cache hit cũng không chạy strategy nên sẽ che lỗi)
        Strategies.CachePolicyOverride = StrategyCachePolicy.Disabled;
        var key = NewKey();
        var first = await PostDecisionAsync(DecisionRequests.Valid(), key);
        Clock.Advance(TimeSpan.FromMinutes(5));

        var second = await PostDecisionAsync(DecisionRequests.Valid(), key);

        first.Status.Should().Be(HttpStatusCode.OK);
        second.Status.Should().Be(HttpStatusCode.OK);
        second.Bytes.Should().Equal(first.Bytes, "replay trả đúng response đã lưu, giống từng byte");
        (await ReadExecutionsAsync()).Should().ContainSingle();
        Strategies.InvocationCount.Should().Be(1, "strategy không chạy lại khi replay");
    }

    /// <summary><c>demand</c>, <c>requestedAt</c> và <c>metadata</c> không nằm trong <c>request_hash</c> nên đổi chúng vẫn là replay, không phải 409.</summary>
    [Fact]
    [Trait("CatalogId", "IT-02")]
    public async Task PostDecision_SameKeyOnlyDemandRequestedAtMetadataChanged_Replays()
    {
        Strategies.CachePolicyOverride = StrategyCachePolicy.Disabled;
        var key = NewKey();
        var first = await PostDecisionAsync(DecisionRequests.Valid(), key);
        var changed = DecisionRequests.Mutated(root =>
        {
            var demand = V2Samples.Object(root["demand"]);
            demand["occupancyRate"] = 0.1;
            demand["waitlistCount"] = 9;
            root["requestedAt"] = "2026-11-18T09:45:00+07:00";
            V2Samples.Object(root["metadata"])["promotionCode"] = "SUMMER";
        });

        var second = await PostDecisionAsync(changed, key);

        second.Status.Should().Be(HttpStatusCode.OK);
        second.Bytes.Should().Equal(first.Bytes);
        (await ReadExecutionsAsync()).Should().ContainSingle();
        Strategies.InvocationCount.Should().Be(1);
    }

    /// <summary>Cùng key nhưng <c>totalPrice</c> khác: 409 <c>IDEMPOTENCY_KEY_REUSE</c>, hàng gốc giữ nguyên.</summary>
    [Fact]
    [Trait("CatalogId", "IT-03")]
    public async Task PostDecision_SameKeyDifferentPayload_Returns409IdempotencyKeyReuse()
    {
        var key = NewKey();
        var first = await PostDecisionAsync(DecisionRequests.Valid(), key);
        var different = DecisionRequests.Mutated(root => root["totalPrice"] = 100_000);

        var second = await PostDecisionAsync(different, key);

        second.Status.Should().Be(HttpStatusCode.Conflict);
        second.ContentType.Should().Be("application/problem+json");
        second.Problem.GetProperty("code").GetString().Should().Be("IDEMPOTENCY_KEY_REUSE");
        var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
        row.DecisionId.Should().Be(first.Result.DecisionId);
        Strategies.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// 50 <see cref="HttpClient"/> cùng gửi một request với cùng key sau một cổng xuất phát chung: đúng một hàng audit, cả 50 response là 200 và giống nhau
    /// (bên thua tranh chấp đọc lại bản của bên thắng nên cùng <c>decisionId</c>).
    /// </summary>
    [Fact]
    [Trait("CatalogId", "IT-04")]
    public async Task PostDecision_FiftyParallelSameKey_OneRecordAndIdenticalResponses()
    {
        var key = NewKey();
        var body = DecisionRequests.Valid();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clients = Enumerable.Range(0, Concurrency).Select(_ => CreateApiClient()).ToList();
        try
        {
            var requests = clients
                .Select(client => Task.Run(async () =>
                {
                    await startGate.Task;
                    return await PostDecisionAsync(body, key, client: client);
                }))
                .ToList();

            startGate.SetResult();
            var responses = await Task.WhenAll(requests);

            responses.Select(response => response.Status).Should().OnlyContain(status => status == HttpStatusCode.OK);
            var winner = responses[0].Result.DecisionId;
            responses.Select(response => response.Result.DecisionId).Should().OnlyContain(id => id == winner);
            responses.Select(response => response.Body).Distinct().Should().ContainSingle("50 response phải giống nhau");
            var row = (await ReadExecutionsAsync()).Should().ContainSingle().Subject;
            row.DecisionId.Should().Be(winner);
        }
        finally
        {
            clients.ForEach(client => client.Dispose());
        }
    }
}
