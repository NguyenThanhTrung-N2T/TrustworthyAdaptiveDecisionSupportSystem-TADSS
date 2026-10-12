using System.Diagnostics;
using System.Net;
using FluentAssertions;
using TADSS.Application.Common;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Observability;

/// <summary>
/// OB-5: việc shadow chạy trên worker nền nên <c>tadss.shadow.execute</c> là gốc của một trace mới, có <see cref="ActivityLink"/> tới span
/// <c>tadss.decision.evaluate</c> của request <c>PRIMARY</c> sinh ra nó.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class TelemetryShadowSpanTests : TelemetryTestBase
{
    public TelemetryShadowSpanTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraConfiguration =>
    [
        new("Tadss:Shadow:Enabled", "true"),
        new("Tadss:Shadow:Strategy", "MockAlternativeDecisionStrategy"),
        new("Tadss:Shadow:SampleRate", "1"),
    ];

    /// <summary>Span shadow có link tới span evaluate của request gốc, không phải con của nó, và mang cùng correlation id.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task ShadowSpan_LinksToRequest()
    {
        await Infrastructure.FlushRedisAsync();

        var response = await PostDecisionAsync(UniqueRequest(7), NewKey(), "corr-otel-shadow");

        response.Status.Should().Be(HttpStatusCode.OK);
        var trace = await WaitForTraceAsync("corr-otel-shadow");
        var evaluate = trace.Single(span => span.OperationName == TadssActivitySource.DecisionEvaluate);
        var shadow = await WaitForShadowSpanAsync("corr-otel-shadow");
        shadow.TraceId.Should().NotBe(evaluate.TraceId, "việc shadow là gốc của trace mới");
        shadow.ParentSpanId.ToString().Should().Be(default(ActivitySpanId).ToString());
        shadow.Links.Should().ContainSingle().Which.Context.Should().Match<ActivityContext>(
            link => link.TraceId == evaluate.TraceId && link.SpanId == evaluate.SpanId);
    }

    private async Task<Activity> WaitForShadowSpanAsync(string correlationId)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (true)
        {
            var shadow = Spans.FirstOrDefault(span =>
                span.OperationName == TadssActivitySource.ShadowExecute
                && Equals(span.GetTagItem(TadssActivitySource.TagCorrelationId), correlationId));
            if (shadow is not null)
            {
                return shadow;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Chưa thấy span shadow của correlation id {correlationId} sau {SettleTimeout.TotalSeconds} giây.");
            }

            await Task.Delay(50);
        }
    }
}
