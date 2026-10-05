using FluentAssertions;
using TADSS.Application.Common;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Common;

/// <summary>
/// UT-07 (phần metric): <see cref="TadssMetrics"/> khai báo meter <c>TADSS.Runtime</c> và các counter với đúng tên, đúng nhãn
/// (gói 5.5 dựa vào tên này để kiểm dashboard).
/// </summary>
public class TadssMetricsTests
{
    public static TheoryData<string, string, string, string> Cases => new()
    {
        { TadssMetrics.ConfigReloadFailures, "tadss.config.reload_failures", "section", "Tadss:Routing" },
        { TadssMetrics.StrategyAbandoned, "tadss.strategy.abandoned", "strategy", "StrategyA" },
        { TadssMetrics.CacheRequests, "tadss.cache.requests", "result", TadssMetrics.CacheResultHit },
        { TadssMetrics.ShadowDropped, "tadss.shadow.dropped", "reason", "queue_full" },
        { TadssMetrics.ShadowExecutions, "tadss.shadow.executions", "outcome", TadssMetrics.ShadowOutcomeFailed }
    };

    /// <summary>Gọi <c>Record*</c> tương ứng với từng instrument.</summary>
    private static void Record(TadssMetrics metrics, string instrument, string tagValue)
    {
        switch (instrument)
        {
            case "tadss.config.reload_failures":
                metrics.RecordConfigReloadFailure(tagValue);
                break;
            case "tadss.strategy.abandoned":
                metrics.RecordStrategyAbandoned(tagValue);
                break;
            case "tadss.cache.requests":
                metrics.RecordCacheRequest(tagValue);
                break;
            case "tadss.shadow.dropped":
                metrics.RecordShadowDropped();
                break;
            case "tadss.shadow.executions":
                metrics.RecordShadowExecution(tagValue);
                break;
            default:
                throw new InvalidOperationException($"Instrument không biết: {instrument}");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("CatalogId", "UT-07")]
    public void Counters_Record_ExpectedNamesAndTags_Theory(string constant, string expectedName, string tagKey, string tagValue)
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);

        Record(metrics, expectedName, tagValue);
        Record(metrics, expectedName, tagValue);

        constant.Should().Be(expectedName);
        metrics.Meter.Name.Should().Be("TADSS.Runtime");
        TadssMetrics.MeterName.Should().Be("TADSS.Runtime");
        recorder.Sum(expectedName, (tagKey, tagValue)).Should().Be(2);
        recorder.Sum(expectedName, (tagKey, "nhan-khac")).Should().Be(0, "nhãn khác giá trị thì không được cộng");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void TagValueConstants_HaveContractValues()
    {
        TadssMetrics.CacheResultHit.Should().Be("hit");
        TadssMetrics.CacheResultMiss.Should().Be("miss");
        TadssMetrics.CacheResultBypass.Should().Be("bypass");
        TadssMetrics.ShadowOutcomeRecorded.Should().Be("recorded");
        TadssMetrics.ShadowOutcomeFailed.Should().Be("failed");
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void Recorders_AreIsolatedPerMeterInstance()
    {
        using var first = new TadssMetrics();
        using var second = new TadssMetrics();
        using var firstRecorder = new MetricRecorder(first.Meter);
        using var secondRecorder = new MetricRecorder(second.Meter);

        first.RecordShadowDropped();

        firstRecorder.Sum(TadssMetrics.ShadowDropped).Should().Be(1);
        secondRecorder.Sum(TadssMetrics.ShadowDropped).Should().Be(0, "recorder chỉ lọc theo đúng instance Meter");
    }
}
