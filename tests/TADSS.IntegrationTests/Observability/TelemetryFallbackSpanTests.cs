using System.Diagnostics;
using System.Net;
using FluentAssertions;
using TADSS.Application.Common;
using TADSS.Infrastructure.FaultInjection;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Observability;

/// <summary>
/// OB-5: strategy ném lỗi (tiêm lỗi <c>Throw</c>, chỉ môi trường Testing) → request vẫn 200 với fallback; span <c>tadss.strategy.execute</c> ghi
/// <c>tadss.outcome = fallback</c> và <c>tadss.fallback_reason = exception</c>, và không thuộc tính nào mang message của exception.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class TelemetryFallbackSpanTests : TelemetryTestBase
{
    public TelemetryFallbackSpanTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ExtraConfiguration =>
    [
        new("Tadss:Testing:FaultInjection:Enabled", "true"),
        new("Tadss:Testing:FaultInjection:Profile", "Throw"),
    ];

    /// <summary>Strategy ném lỗi → span strategy đánh dấu fallback do exception, không rò message.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task PostDecision_Fallback_SpanMarksOutcome()
    {
        await Infrastructure.FlushRedisAsync();

        var response = await PostDecisionAsync(UniqueRequest(6), NewKey(), "corr-otel-fallback");

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Result.FallbackUsed.Should().BeTrue();
        var trace = await WaitForTraceAsync("corr-otel-fallback");
        var execute = trace.Should().ContainSingle(span => span.OperationName == TadssActivitySource.StrategyExecute).Subject;
        execute.GetTagItem(TadssActivitySource.TagOutcome).Should().Be(TadssMetricNames.PipelineOutcomeFallback);
        execute.GetTagItem(TadssActivitySource.TagFallbackReason).Should().Be(TadssMetricNames.FallbackReasonException);
        var evaluate = trace.Single(span => span.OperationName == TadssActivitySource.DecisionEvaluate);
        evaluate.GetTagItem(TadssActivitySource.TagExecutionType).Should().Be("FALLBACK");
        trace.SelectMany(span => span.TagObjects.Select(tag => $"{tag.Value}").Append(span.StatusDescription ?? string.Empty))
            .Should().NotContain(text => text.Contains(FaultInjectionStrategyDecorator.ThrowMessage, StringComparison.Ordinal));
    }
}
