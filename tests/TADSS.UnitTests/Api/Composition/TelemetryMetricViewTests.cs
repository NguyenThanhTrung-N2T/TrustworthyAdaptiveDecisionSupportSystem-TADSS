using System.Diagnostics.Metrics;
using FluentAssertions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using TADSS.Application.Common;
using TADSS.Runtime.Api.Composition;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// OB-5: View của composition áp đúng biên bucket <c>DurationBucketBoundariesMs</c> (O-05) cho histogram có đơn vị <c>ms</c> và không đụng tới instrument khác.
/// Chạy trên <see cref="MeterProvider"/> thật với meter có <b>tên riêng của test</b>: meter trùng tên của các host khác trong cùng process làm SDK gộp luồng metric
/// (và vô hiệu hóa luồng khi một meter bị dispose), nên không thể kiểm bằng meter <c>TADSS.Runtime</c> dùng chung.
/// </summary>
public class TelemetryMetricViewTests
{
    private static readonly double[] DefaultBounds = [0, 5, 10, 25, 50, 75, 100, 250, 500, 750, 1000, 2500, 5000, 7500, 10000];

    private static List<double> CollectBounds(string meterName, string instrumentName, string unit)
    {
        var exported = new List<MetricSnapshot>();
        using var meter = new Meter(meterName);
        using var provider = OpenTelemetry.Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddView(TelemetryServiceCollectionExtensions.DurationBucketView)
            .AddInMemoryExporter(exported)
            .Build();
        var histogram = meter.CreateHistogram<double>(instrumentName, unit);
        histogram.Record(123);

        provider.ForceFlush(5000).Should().BeTrue();

        var point = exported.Should().ContainSingle(metric => metric.Name == instrumentName).Subject.MetricPoints.Should().ContainSingle().Subject;
        var bounds = new List<double>();
        foreach (var bucket in point.GetHistogramBuckets())
        {
            if (!double.IsPositiveInfinity(bucket.ExplicitBound))
            {
                bounds.Add(bucket.ExplicitBound);
            }
        }

        return bounds;
    }

    /// <summary>Histogram đơn vị <c>ms</c> nhận đúng biên của O-05 (có 150, 500, 700 để KPI là phép đếm chính xác).</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void DurationBucketView_MillisecondHistogram_UsesConfiguredBoundaries()
    {
        var bounds = CollectBounds("TADSS.Tests.View.Ms", TadssMetricNames.DecisionDuration, TadssMetricNames.UnitMilliseconds);

        bounds.Should().Equal(TadssMetricNames.DurationBucketBoundariesMs);
        bounds.Should().Contain([150d, 500d, 700d]);
    }

    /// <summary>Histogram đơn vị khác <c>ms</c> giữ biên mặc định của SDK.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void DurationBucketView_OtherUnit_KeepsDefaultBoundaries()
    {
        var bounds = CollectBounds("TADSS.Tests.View.Other", "tests.other.duration", "s");

        bounds.Should().Equal(DefaultBounds);
    }

    /// <summary>Mọi instrument khai báo trong <see cref="TadssMetricNames.AllInstrumentNames"/> có đơn vị <c>ms</c> đều thuộc histogram thời gian (khớp View).</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public void DurationBucketView_AppliesOnlyToDeclaredDurationHistograms()
    {
        using var metrics = new TadssMetrics();
        var seen = new Dictionary<string, string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter))
            {
                seen[instrument.Name] = TelemetryServiceCollectionExtensions.DurationBucketView(instrument) is null ? null : instrument.Unit;
            }
        };
        listener.Start();

        seen.Where(pair => pair.Value is not null).Select(pair => pair.Key)
            .Should().BeEquivalentTo([TadssMetricNames.DecisionDuration, TadssMetricNames.PipelineDuration]);
        seen.Keys.Should().BeEquivalentTo(TadssMetricNames.AllInstrumentNames);
    }
}
