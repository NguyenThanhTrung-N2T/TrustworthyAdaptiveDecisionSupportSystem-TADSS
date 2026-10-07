using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using TADSS.Application.Common;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Architecture;

/// <summary>
/// OB-1: dashboard Grafana (<c>docker/grafana/dashboards/tadss-runtime.json</c>) chỉ tham chiếu metric có thật. Mọi định danh <c>tadss_*</c> trong biểu thức PromQL phải ánh xạ
/// (theo quy tắc đổi tên đã xác nhận ở spike S-2) về một instrument của meter <c>TADSS.Runtime</c>; loại instrument và đơn vị đọc từ chính instrument tạo ra (không khai báo lặp);
/// mọi định danh <c>k6_*</c> nằm trong <see cref="K6MetricNames.All"/>; mọi instrument khai báo đều được dashboard dùng.
/// </summary>
public class DashboardMetricReferenceTests
{
    private static readonly Regex MetricIdentifier = new(@"\b((?:tadss|k6)_[a-z0-9_]+)\b", RegexOptions.Compiled);
    private static readonly string[] AllowedDatasourceUids = ["prometheus", "tempo", "loki"];

    private static JsonDocument LoadDashboard() =>
        JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Resolve("docker", "grafana", "dashboards", "tadss-runtime.json")));

    private static IEnumerable<JsonElement> Panels(JsonElement root) =>
        root.GetProperty("panels").EnumerateArray().Where(panel => panel.GetProperty("type").GetString() != "row");

    private static List<string> Expressions(JsonElement root) =>
        Panels(root)
            .SelectMany(panel => panel.TryGetProperty("targets", out var targets) ? targets.EnumerateArray().ToList() : [])
            .Select(target => target.TryGetProperty("expr", out var expr) ? expr.GetString() : null)
            .OfType<string>()
            .ToList();

    private static List<string> MetricIdentifiersIn(IEnumerable<string> expressions) =>
        expressions.SelectMany(expression => MetricIdentifier.Matches(expression).Select(match => match.Groups[1].Value)).Distinct().ToList();

    /// <summary>Instrument thật của meter TADSS → tên series Prometheus hợp lệ (đọc loại và đơn vị từ instrument).</summary>
    private static Dictionary<string, string> PrometheusNamesByInstrument()
    {
        using var metrics = new TadssMetrics();
        var seen = new List<Instrument>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter))
            {
                seen.Add(instrument);
            }
        };
        listener.Start();

        var result = new Dictionary<string, string>();
        foreach (var instrument in seen)
        {
            var typeName = instrument.GetType().Name;
            var kind = typeName.StartsWith("Histogram", StringComparison.Ordinal) ? PrometheusInstrumentKind.Histogram
                : typeName.StartsWith("Counter", StringComparison.Ordinal) ? PrometheusInstrumentKind.Counter
                : PrometheusInstrumentKind.Gauge;
            foreach (var series in PrometheusNameMapper.ToPrometheusNames(instrument.Name, kind, instrument.Unit))
            {
                result[series] = instrument.Name;
            }
        }

        return result;
    }

    /// <summary>Mọi <c>tadss_*</c> trong biểu thức ánh xạ được về một instrument; mọi <c>k6_*</c> ∈ <see cref="K6MetricNames.All"/>; không có tiền tố metric nào khác.</summary>
    [Fact]
    [Trait("CatalogId", "OB-1")]
    public void Dashboard_ReferencesOnlyDeclaredMetrics()
    {
        using var dashboard = LoadDashboard();
        var known = PrometheusNamesByInstrument();
        var identifiers = MetricIdentifiersIn(Expressions(dashboard.RootElement));

        identifiers.Should().NotBeEmpty();
        var unknownTadss = identifiers.Where(id => id.StartsWith("tadss_", StringComparison.Ordinal) && !known.ContainsKey(id)).ToList();
        var unknownK6 = identifiers.Where(id => id.StartsWith(K6MetricNames.Prefix, StringComparison.Ordinal) && !K6MetricNames.All.Contains(id)).ToList();
        unknownTadss.Should().BeEmpty("mọi định danh tadss_* phải ánh xạ về instrument khai báo ở TadssMetricNames");
        unknownK6.Should().BeEmpty("mọi định danh k6_* phải nằm trong K6MetricNames.All");
    }

    /// <summary>
    /// Dashboard có panel cho mọi instrument khai báo (<see cref="TadssMetricNames.AllInstrumentNames"/>), cho k6, ba tỉ lệ ngưỡng đọc thẳng từ bucket <c>le</c> (SC1, SC2, ngân sách pipeline),
    /// đủ bảy hàng; mọi panel có tiêu đề + mô tả, datasource thuộc UID có sẵn của otel-lgtm; có biến <c>instance</c>, annotation <c>chaos</c>, làm mới 5 giây, khoảng mặc định 30 phút.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-1")]
    public void Dashboard_HasRequiredPanels()
    {
        using var dashboard = LoadDashboard();
        var root = dashboard.RootElement;
        var known = PrometheusNamesByInstrument();
        var expressions = Expressions(root);
        var used = MetricIdentifiersIn(expressions).Where(known.ContainsKey).Select(id => known[id]).ToHashSet();

        used.Should().BeEquivalentTo(TadssMetricNames.AllInstrumentNames, "mỗi instrument phải có ít nhất một panel");
        expressions.Should().Contain(expression => expression.Contains("k6_http_reqs_total"));
        expressions.Should().Contain(expression => expression.Contains("le=\"150\""), "tỉ lệ ≤ 150 ms đọc thẳng từ bucket");
        expressions.Should().Contain(expression => expression.Contains("le=\"700\""), "tỉ lệ ≤ 700 ms đọc thẳng từ bucket");
        expressions.Should().Contain(expression => expression.Contains("le=\"500\""), "tỉ lệ pipeline ≤ 500 ms đọc thẳng từ bucket");
        expressions.Should().Contain(expression => expression.Contains("histogram_quantile"));

        root.GetProperty("panels").EnumerateArray().Where(panel => panel.GetProperty("type").GetString() == "row").Should().HaveCount(7);
        foreach (var panel in Panels(root))
        {
            var title = panel.GetProperty("title").GetString();
            title.Should().NotBeNullOrWhiteSpace();
            panel.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace($"panel '{title}' cần mô tả");
            AllowedDatasourceUids.Should().Contain(panel.GetProperty("datasource").GetProperty("uid").GetString());
        }

        root.GetProperty("refresh").GetString().Should().Be("5s");
        root.GetProperty("time").GetProperty("from").GetString().Should().Be("now-30m");
        root.GetProperty("templating").GetProperty("list").EnumerateArray().Select(variable => variable.GetProperty("name").GetString()).Should().Contain("instance");
        root.GetProperty("annotations").GetProperty("list").EnumerateArray().Select(annotation => annotation.GetProperty("name").GetString()).Should().Contain("chaos");
    }

    /// <summary>Quy tắc đổi tên của S-2: counter <c>_total</c>, histogram <c>ms</c> thêm <c>_milliseconds</c> + bucket/sum/count, gauge giữ nguyên.</summary>
    [Theory]
    [Trait("CatalogId", "OB-1")]
    [InlineData("tadss.decisions", PrometheusInstrumentKind.Counter, null, "tadss_decisions_total")]
    [InlineData("tadss.decision.duration", PrometheusInstrumentKind.Histogram, "ms", "tadss_decision_duration_milliseconds_bucket,tadss_decision_duration_milliseconds_sum,tadss_decision_duration_milliseconds_count")]
    [InlineData("tadss.circuit.state", PrometheusInstrumentKind.Gauge, null, "tadss_circuit_state")]
    public void PrometheusNameMapper_Theory(string instrument, PrometheusInstrumentKind kind, string? unit, string expected)
    {
        PrometheusNameMapper.ToPrometheusNames(instrument, kind, unit).Should().Equal(expected.Split(','));
    }
}
