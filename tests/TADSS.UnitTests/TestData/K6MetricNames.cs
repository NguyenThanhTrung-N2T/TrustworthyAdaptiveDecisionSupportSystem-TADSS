namespace TADSS.UnitTests.TestData;

/// <summary>
/// Series metric của k6 (xuất qua output OpenTelemetry với tiền tố <c>k6_</c>, spike S-3) mà dashboard được dùng — nơi duy nhất khai báo (OB-1).
/// Dashboard tham chiếu <c>k6_*</c> ngoài danh sách này làm test đỏ.
/// </summary>
internal static class K6MetricNames
{
    /// <summary>Tiền tố k6 (<c>K6_OTEL_METRIC_PREFIX</c>).</summary>
    public const string Prefix = "k6_";

    /// <summary>Tổng số request.</summary>
    public const string HttpReqsTotal = "k6_http_reqs_total";

    /// <summary>Bucket thời gian request phía client (ms).</summary>
    public const string HttpReqDurationBucket = "k6_http_req_duration_milliseconds_bucket";

    /// <summary>Mọi series k6 dashboard được dùng.</summary>
    public static IReadOnlyList<string> All { get; } = [HttpReqsTotal, HttpReqDurationBucket];
}
