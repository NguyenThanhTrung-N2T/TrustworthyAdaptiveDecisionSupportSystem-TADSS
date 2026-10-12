namespace TADSS.UnitTests.TestData;

/// <summary>Loại instrument theo cách OpenTelemetry → Prometheus đổi tên (spike S-2).</summary>
public enum PrometheusInstrumentKind
{
    /// <summary>Counter đơn điệu: thêm <c>_total</c>.</summary>
    Counter,

    /// <summary>Histogram: thêm <c>_bucket</c>, <c>_sum</c>, <c>_count</c>.</summary>
    Histogram,

    /// <summary>Gauge (kể cả observable gauge): giữ nguyên tên.</summary>
    Gauge,
}

/// <summary>
/// Quy tắc đổi tên metric từ OpenTelemetry sang Prometheus đã xác nhận ở spike S-2 (OB-1): dấu chấm → gạch dưới; đơn vị <c>ms</c> thêm hậu tố <c>_milliseconds</c>;
/// counter thêm <c>_total</c>; histogram thêm <c>_bucket</c>/<c>_sum</c>/<c>_count</c>; gauge giữ nguyên.
/// </summary>
internal static class PrometheusNameMapper
{
    private const string MillisecondsUnit = "ms";

    /// <summary>Tập tên series Prometheus hợp lệ của một instrument.</summary>
    public static IReadOnlyList<string> ToPrometheusNames(string instrumentName, PrometheusInstrumentKind kind, string? unit)
    {
        var name = instrumentName.Replace('.', '_');
        if (unit == MillisecondsUnit)
        {
            name += "_milliseconds";
        }

        return kind switch
        {
            PrometheusInstrumentKind.Counter => [$"{name}_total"],
            PrometheusInstrumentKind.Histogram => [$"{name}_bucket", $"{name}_sum", $"{name}_count"],
            _ => [name],
        };
    }
}
