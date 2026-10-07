using System.Diagnostics.Metrics;
using FluentAssertions;
using TADSS.Application.Common;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Application.Common;

/// <summary>
/// UT-07 / OB-5 (phần metric): <see cref="TadssMetrics"/> khai báo meter <c>TADSS.Runtime</c> và các instrument với đúng tên, đúng nhãn
/// (dashboard Grafana và OB-1 dựa vào tên khai báo ở <see cref="TadssMetricNames"/>).
/// </summary>
public class TadssMetricsTests
{
    private static readonly Guid BranchA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid BranchB = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public static TheoryData<string, string, string, string> Cases => new()
    {
        { TadssMetricNames.ConfigReloadFailures, "tadss.config.reload_failures", "section", "Tadss:Routing" },
        { TadssMetricNames.StrategyAbandoned, "tadss.strategy.abandoned", "strategy", "StrategyA" },
        { TadssMetricNames.CacheRequests, "tadss.cache.requests", "result", TadssMetricNames.CacheResultHit },
        { TadssMetricNames.ShadowDropped, "tadss.shadow.dropped", "reason", "queue_full" },
        { TadssMetricNames.ShadowExecutions, "tadss.shadow.executions", "outcome", TadssMetricNames.ShadowOutcomeFailed },
        { TadssMetricNames.Overrides, "tadss.overrides", "action", "APPROVE" },
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
            case "tadss.overrides":
                metrics.RecordOverride(tagValue);
                break;
            default:
                throw new InvalidOperationException($"Instrument không biết: {instrument}");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("CatalogId", "UT-07")]
    [Trait("CatalogId", "OB-5")]
    public void Counters_Record_ExpectedNamesAndTags_Theory(string constant, string expectedName, string tagKey, string tagValue)
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);

        Record(metrics, expectedName, tagValue);
        Record(metrics, expectedName, tagValue);

        constant.Should().Be(expectedName);
        metrics.Meter.Name.Should().Be("TADSS.Runtime");
        TadssMetricNames.MeterName.Should().Be("TADSS.Runtime");
        recorder.Sum(expectedName, (tagKey, tagValue)).Should().Be(2);
        recorder.Sum(expectedName, (tagKey, "nhan-khac")).Should().Be(0, "nhãn khác giá trị thì không được cộng");
    }

    /// <summary>Counter nhiều nhãn: <c>tadss.decisions</c> và <c>tadss.fallbacks</c> mang đúng tên và giá trị từng nhãn.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void Record_MultiTagCounters_ExpectedNamesAndTags()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);

        metrics.RecordDecision("APPROVED", "DefaultDecisionStrategy", "AUTOMATED");
        metrics.RecordFallback(TadssMetricNames.FallbackReasonCircuitOpen, "DefaultDecisionStrategy");

        recorder.Sum(
            "tadss.decisions",
            ("status", "APPROVED"),
            ("strategy", "DefaultDecisionStrategy"),
            ("execution_type", "AUTOMATED")).Should().Be(1);
        recorder.Sum("tadss.fallbacks", ("reason", "circuit_open"), ("strategy", "DefaultDecisionStrategy")).Should().Be(1);
        recorder.Sum("tadss.decisions", ("status", "REJECTED")).Should().Be(0);
    }

    /// <summary>Histogram thời gian: đơn vị <c>ms</c>, đúng tên và nhãn; <c>status_code</c> là chuỗi.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void Record_Histograms_ExpectedNamesTagsAndUnit()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var units = CaptureInstrumentUnits(metrics);

        metrics.RecordRequestDuration("POST api/v2/decisions", 200, 42.5);
        metrics.RecordPipelineDuration("DefaultDecisionStrategy", TadssMetricNames.PipelineOutcomeAutomated, 7.25);

        recorder.Values("tadss.decision.duration", ("endpoint", "POST api/v2/decisions"), ("status_code", "200")).Should().Equal(42.5);
        recorder.Values("tadss.pipeline.duration", ("strategy", "DefaultDecisionStrategy"), ("outcome", "automated")).Should().Equal(7.25);
        units["tadss.decision.duration"].Should().Be("ms");
        units["tadss.pipeline.duration"].Should().Be("ms");
    }

    /// <summary>Tập tên instrument thực sự được phát bằng đúng <see cref="TadssMetricNames.AllInstrumentNames"/> (dashboard tra theo danh sách này).</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void Instruments_AllDeclaredNamesPublished()
    {
        using var metrics = new TadssMetrics();

        var published = CaptureInstrumentUnits(metrics).Keys;

        published.Should().BeEquivalentTo(TadssMetricNames.AllInstrumentNames);
        TadssMetricNames.AllInstrumentNames.Should().OnlyHaveUniqueItems().And.OnlyContain(name => name.StartsWith("tadss."));
    }

    /// <summary>Biên bucket tăng dần và chứa 150, 500, 700 (ngưỡng KPI SC1, SC2) để đếm theo bucket là chính xác.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void DurationBuckets_IncludeKpiThresholds()
    {
        var boundaries = TadssMetricNames.DurationBucketBoundariesMs;

        boundaries.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        boundaries.Should().Contain([150d, 500d, 700d]);
    }

    /// <summary>Gauge breaker phát số đo theo từng strategy từ nguồn đã đăng ký; chưa đăng ký thì không phát gì.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void CircuitStateGauge_EmitsRegisteredSourceOnly()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);

        recorder.ObserveGauge("tadss.circuit.state").Should().BeEmpty("chưa có nguồn thì không phát số đo");

        metrics.RegisterCircuitStateSource(() =>
        [
            new KeyValuePair<string, CircuitStateValue>("A", CircuitStateValue.Open),
            new KeyValuePair<string, CircuitStateValue>("B", CircuitStateValue.Closed),
        ]);
        var samples = recorder.ObserveGauge("tadss.circuit.state");

        samples.Should().HaveCount(2);
        samples.Single(s => s.Tags["strategy"] == "A").Value.Should().Be(2);
        samples.Single(s => s.Tags["strategy"] == "B").Value.Should().Be(0);
    }

    /// <summary>Gauge breaker Redis không nhãn; nguồn trả <c>null</c> (không dùng Redis) thì không phát số đo.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void CacheCircuitStateGauge_NullSourceValueEmitsNothing()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        CircuitStateValue? state = CircuitStateValue.HalfOpen;
        metrics.RegisterCacheCircuitStateSource(() => state);

        recorder.ObserveGauge("tadss.cache.circuit.state").Should().ContainSingle().Which.Value.Should().Be(1);

        state = null;
        recorder.ObserveGauge("tadss.cache.circuit.state").Should().BeEmpty();
    }

    /// <summary>Gauge hàng chờ báo theo chi nhánh từ ảnh chụp; thay ảnh chụp thì chi nhánh vắng mặt ngừng phát.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void PendingApprovalsGauge_FollowsReplacedSnapshot()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);

        metrics.ReplacePendingApprovals(new Dictionary<Guid, long> { [BranchA] = 2, [BranchB] = 1 });
        var first = recorder.ObserveGauge("tadss.pending_approvals");
        metrics.ReplacePendingApprovals(new Dictionary<Guid, long> { [BranchA] = 5 });
        var second = recorder.ObserveGauge("tadss.pending_approvals");

        first.Should().HaveCount(2);
        first.Single(s => s.Tags["branch"] == BranchA.ToString("D")).Value.Should().Be(2);
        second.Should().ContainSingle().Which.Value.Should().Be(5);
    }

    [Fact]
    [Trait("CatalogId", "UT-07")]
    public void TagValueConstants_HaveContractValues()
    {
        TadssMetricNames.CacheResultHit.Should().Be("hit");
        TadssMetricNames.CacheResultMiss.Should().Be("miss");
        TadssMetricNames.CacheResultBypass.Should().Be("bypass");
        TadssMetricNames.ShadowOutcomeRecorded.Should().Be("recorded");
        TadssMetricNames.ShadowOutcomeFailed.Should().Be("failed");
        TadssMetricNames.PipelineOutcomeAutomated.Should().Be("automated");
        TadssMetricNames.PipelineOutcomeFallback.Should().Be("fallback");
        TadssMetricNames.PipelineOutcomeTimeout.Should().Be("timeout");
        TadssMetricNames.FallbackReasonTimeout.Should().Be("timeout");
        TadssMetricNames.FallbackReasonException.Should().Be("exception");
        TadssMetricNames.FallbackReasonCircuitOpen.Should().Be("circuit_open");
        ((int)CircuitStateValue.Closed).Should().Be(0);
        ((int)CircuitStateValue.HalfOpen).Should().Be(1);
        ((int)CircuitStateValue.Open).Should().Be(2);
        ((int)CircuitStateValue.Isolated).Should().Be(3);
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

        firstRecorder.Sum(TadssMetricNames.ShadowDropped).Should().Be(1);
        secondRecorder.Sum(TadssMetricNames.ShadowDropped).Should().Be(0, "recorder chỉ lọc theo đúng instance Meter");
    }

    /// <summary>Liệt kê instrument (tên → đơn vị) mà meter của <paramref name="metrics"/> phát ra.</summary>
    private static Dictionary<string, string?> CaptureInstrumentUnits(TadssMetrics metrics)
    {
        var units = new Dictionary<string, string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter))
            {
                units[instrument.Name] = instrument.Unit;
            }
        };
        listener.Start();
        return units;
    }
}
