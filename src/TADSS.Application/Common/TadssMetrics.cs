using System.Diagnostics.Metrics;

namespace TADSS.Application.Common;

/// <summary>
/// Meter <c>TADSS.Runtime</c> (A-10): nơi <b>duy nhất</b> khai báo tên metric và nhãn của runtime; mã khác chỉ gọi <c>Record*</c>,
/// không viết literal tên metric. Singleton, sở hữu một <see cref="System.Diagnostics.Metrics.Meter"/> và dispose nó cùng host.
/// Chỉ dùng BCL (<c>System.Diagnostics.Metrics</c>) nên Application dùng được mà không phụ thuộc exporter; gói 5.5 gắn OpenTelemetry
/// qua <c>AddMeter(MeterName)</c>.
/// </summary>
public sealed class TadssMetrics : IDisposable
{
    /// <summary>Tên meter của runtime.</summary>
    public const string MeterName = "TADSS.Runtime";

    /// <summary>Số lần nạp lại cấu hình thất bại (giữ cấu hình cũ), nhãn <c>section</c>.</summary>
    public const string ConfigReloadFailures = "tadss.config.reload_failures";

    /// <summary>Số lần một lượt chạy strategy bị bỏ rơi vì token hủy khi strategy chưa xong, nhãn <c>strategy</c>.</summary>
    public const string StrategyAbandoned = "tadss.strategy.abandoned";

    /// <summary>Số lần tra cache strategy, nhãn <c>result</c> (<see cref="CacheResultHit"/>, <see cref="CacheResultMiss"/>, <see cref="CacheResultBypass"/>).</summary>
    public const string CacheRequests = "tadss.cache.requests";

    /// <summary>Số việc shadow bị bỏ vì hàng đợi đầy, nhãn <c>reason</c>.</summary>
    public const string ShadowDropped = "tadss.shadow.dropped";

    /// <summary>Số lần chạy shadow, nhãn <c>outcome</c> (<see cref="ShadowOutcomeRecorded"/>, <see cref="ShadowOutcomeFailed"/>).</summary>
    public const string ShadowExecutions = "tadss.shadow.executions";

    /// <summary>Cache có kết quả.</summary>
    public const string CacheResultHit = "hit";

    /// <summary>Cache không có kết quả (hoặc cache lỗi).</summary>
    public const string CacheResultMiss = "miss";

    /// <summary>Bỏ qua Redis vì breaker đang mở.</summary>
    public const string CacheResultBypass = "bypass";

    /// <summary>Việc shadow đã ghi bản ghi <c>SHADOW</c>.</summary>
    public const string ShadowOutcomeRecorded = "recorded";

    /// <summary>Việc shadow thất bại, không có bản ghi.</summary>
    public const string ShadowOutcomeFailed = "failed";

    /// <summary>Lý do bỏ việc shadow: hàng đợi đầy.</summary>
    public const string ShadowDropReasonQueueFull = "queue_full";

    private readonly Counter<long> _configReloadFailures;
    private readonly Counter<long> _strategyAbandoned;
    private readonly Counter<long> _cacheRequests;
    private readonly Counter<long> _shadowDropped;
    private readonly Counter<long> _shadowExecutions;

    /// <summary>Tạo meter và các counter.</summary>
    public TadssMetrics()
    {
        Meter = new Meter(MeterName);
        _configReloadFailures = Meter.CreateCounter<long>(ConfigReloadFailures, description: "Số lần nạp lại cấu hình thất bại.");
        _strategyAbandoned = Meter.CreateCounter<long>(StrategyAbandoned, description: "Số lượt chạy strategy bị bỏ rơi do hủy.");
        _cacheRequests = Meter.CreateCounter<long>(CacheRequests, description: "Số lần tra cache strategy.");
        _shadowDropped = Meter.CreateCounter<long>(ShadowDropped, description: "Số việc shadow bị bỏ.");
        _shadowExecutions = Meter.CreateCounter<long>(ShadowExecutions, description: "Số lần chạy shadow.");
    }

    /// <summary>Meter của runtime (test lọc số đo theo đúng instance này).</summary>
    public Meter Meter { get; }

    /// <summary>Ghi một lần nạp lại cấu hình thất bại của section <paramref name="section"/>.</summary>
    public void RecordConfigReloadFailure(string section) =>
        _configReloadFailures.Add(1, new KeyValuePair<string, object?>("section", section));

    /// <summary>Ghi một lượt chạy của strategy <paramref name="strategyName"/> bị bỏ rơi.</summary>
    public void RecordStrategyAbandoned(string strategyName) =>
        _strategyAbandoned.Add(1, new KeyValuePair<string, object?>("strategy", strategyName));

    /// <summary>Ghi một lần tra cache với kết quả <paramref name="result"/> (hằng <c>CacheResult*</c>).</summary>
    public void RecordCacheRequest(string result) =>
        _cacheRequests.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>Ghi một việc shadow bị bỏ vì hàng đợi đầy.</summary>
    public void RecordShadowDropped() =>
        _shadowDropped.Add(1, new KeyValuePair<string, object?>("reason", ShadowDropReasonQueueFull));

    /// <summary>Ghi một lần chạy shadow với kết quả <paramref name="outcome"/> (hằng <c>ShadowOutcome*</c>).</summary>
    public void RecordShadowExecution(string outcome) =>
        _shadowExecutions.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <inheritdoc />
    public void Dispose() => Meter.Dispose();
}
