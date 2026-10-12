using System.Diagnostics.Metrics;
using System.Globalization;

namespace TADSS.Application.Common;

/// <summary>
/// Meter <c>TADSS.Runtime</c> (A-10, O-05): giữ các instrument và các hàm <c>Record*</c>; tên metric, nhãn và biên histogram khai báo ở
/// <see cref="TadssMetricNames"/> (nơi duy nhất), mã khác không viết literal tên metric. Singleton, sở hữu một
/// <see cref="System.Diagnostics.Metrics.Meter"/> và dispose nó cùng host. Chỉ dùng BCL (<c>System.Diagnostics.Metrics</c>) nên Application
/// dùng được mà không phụ thuộc exporter; composition root gắn OpenTelemetry qua <c>AddMeter(TadssMetricNames.MeterName)</c>.
/// <para>
/// Gauge đọc từ nguồn do adapter đăng ký (trạng thái breaker) hoặc từ ảnh chụp được thay nguyên khối (hàng chờ duyệt) nên callback xuất
/// metric không bao giờ gọi I/O. Chưa có nguồn thì gauge không phát số đo.
/// </para>
/// </summary>
public sealed class TadssMetrics : IDisposable
{
    private readonly Counter<long> _configReloadFailures;
    private readonly Counter<long> _strategyAbandoned;
    private readonly Counter<long> _cacheRequests;
    private readonly Counter<long> _shadowDropped;
    private readonly Counter<long> _shadowExecutions;
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _fallbacks;
    private readonly Counter<long> _overrides;
    private readonly Histogram<double> _requestDuration;
    private readonly Histogram<double> _pipelineDuration;

    private Func<IEnumerable<KeyValuePair<string, CircuitStateValue>>>? _circuitStateSource;
    private Func<CircuitStateValue?>? _cacheCircuitStateSource;
    private IReadOnlyDictionary<Guid, long> _pendingApprovals = new Dictionary<Guid, long>();

    /// <summary>Tạo meter, các counter, histogram và gauge.</summary>
    public TadssMetrics()
    {
        Meter = new Meter(TadssMetricNames.MeterName);
        _configReloadFailures = Meter.CreateCounter<long>(TadssMetricNames.ConfigReloadFailures, description: "Số lần nạp lại cấu hình thất bại.");
        _strategyAbandoned = Meter.CreateCounter<long>(TadssMetricNames.StrategyAbandoned, description: "Số lượt chạy strategy bị bỏ rơi do hủy.");
        _cacheRequests = Meter.CreateCounter<long>(TadssMetricNames.CacheRequests, description: "Số lần tra cache strategy.");
        _shadowDropped = Meter.CreateCounter<long>(TadssMetricNames.ShadowDropped, description: "Số việc shadow bị bỏ.");
        _shadowExecutions = Meter.CreateCounter<long>(TadssMetricNames.ShadowExecutions, description: "Số lần chạy shadow.");
        _decisions = Meter.CreateCounter<long>(TadssMetricNames.Decisions, description: "Số bản ghi PRIMARY mới của chính request.");
        _fallbacks = Meter.CreateCounter<long>(TadssMetricNames.Fallbacks, description: "Số quyết định fallback.");
        _overrides = Meter.CreateCounter<long>(TadssMetricNames.Overrides, description: "Số override mới được ghi.");
        _requestDuration = Meter.CreateHistogram<double>(TadssMetricNames.DecisionDuration, TadssMetricNames.UnitMilliseconds, "Thời gian xử lý request /api/*.");
        _pipelineDuration = Meter.CreateHistogram<double>(TadssMetricNames.PipelineDuration, TadssMetricNames.UnitMilliseconds, "Thời gian pipeline strategy.");
        Meter.CreateObservableGauge(TadssMetricNames.CircuitState, ObserveCircuitState, description: "Trạng thái circuit breaker của từng strategy (0 đóng, 1 nửa mở, 2 mở, 3 cô lập).");
        Meter.CreateObservableGauge(TadssMetricNames.CacheCircuitState, ObserveCacheCircuitState, description: "Trạng thái circuit breaker của Redis (0 đóng, 1 nửa mở, 2 mở, 3 cô lập).");
        Meter.CreateObservableGauge(TadssMetricNames.PendingApprovals, ObservePendingApprovals, description: "Số quyết định đang chờ duyệt theo chi nhánh.");
    }

    /// <summary>Meter của runtime (test lọc số đo theo đúng instance này).</summary>
    public Meter Meter { get; }

    /// <summary>Ghi một lần nạp lại cấu hình thất bại của section <paramref name="section"/>.</summary>
    public void RecordConfigReloadFailure(string section) =>
        _configReloadFailures.Add(1, Tag(TadssMetricNames.LabelSection, section));

    /// <summary>Ghi một lượt chạy của strategy <paramref name="strategyName"/> bị bỏ rơi.</summary>
    public void RecordStrategyAbandoned(string strategyName) =>
        _strategyAbandoned.Add(1, Tag(TadssMetricNames.LabelStrategy, strategyName));

    /// <summary>Ghi một lần tra cache với kết quả <paramref name="result"/> (hằng <c>CacheResult*</c>).</summary>
    public void RecordCacheRequest(string result) =>
        _cacheRequests.Add(1, Tag(TadssMetricNames.LabelResult, result));

    /// <summary>Ghi một việc shadow bị bỏ vì hàng đợi đầy.</summary>
    public void RecordShadowDropped() =>
        _shadowDropped.Add(1, Tag(TadssMetricNames.LabelReason, TadssMetricNames.ShadowDropReasonQueueFull));

    /// <summary>Ghi một lần chạy shadow với kết quả <paramref name="outcome"/> (hằng <c>ShadowOutcome*</c>).</summary>
    public void RecordShadowExecution(string outcome) =>
        _shadowExecutions.Add(1, Tag(TadssMetricNames.LabelOutcome, outcome));

    /// <summary>Ghi một bản <c>PRIMARY</c> mới của chính request (không ghi replay idempotency, thua tranh chấp hay <c>SHADOW</c>).</summary>
    public void RecordDecision(string status, string strategy, string executionType) =>
        _decisions.Add(
            1,
            Tag(TadssMetricNames.LabelStatus, status),
            Tag(TadssMetricNames.LabelStrategy, strategy),
            Tag(TadssMetricNames.LabelExecutionType, executionType));

    /// <summary>Ghi thời gian xử lý một request <c>/api/*</c>; <paramref name="endpoint"/> là mẫu route, không phải đường dẫn thật.</summary>
    public void RecordRequestDuration(string endpoint, int statusCode, double elapsedMs) =>
        _requestDuration.Record(
            elapsedMs,
            Tag(TadssMetricNames.LabelEndpoint, endpoint),
            Tag(TadssMetricNames.LabelStatusCode, statusCode.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Ghi thời gian pipeline của <paramref name="strategy"/> với kết quả <paramref name="outcome"/> (hằng <c>PipelineOutcome*</c>).</summary>
    public void RecordPipelineDuration(string strategy, string outcome, double elapsedMs) =>
        _pipelineDuration.Record(
            elapsedMs,
            Tag(TadssMetricNames.LabelStrategy, strategy),
            Tag(TadssMetricNames.LabelOutcome, outcome));

    /// <summary>Ghi một quyết định fallback (hằng <c>FallbackReason*</c>) của <paramref name="strategy"/>.</summary>
    public void RecordFallback(string reason, string strategy) =>
        _fallbacks.Add(1, Tag(TadssMetricNames.LabelReason, reason), Tag(TadssMetricNames.LabelStrategy, strategy));

    /// <summary>Ghi một override mới với hành động <paramref name="action"/>.</summary>
    public void RecordOverride(string action) =>
        _overrides.Add(1, Tag(TadssMetricNames.LabelAction, action));

    /// <summary>Đăng ký nguồn trạng thái breaker của từng strategy (khóa = tên strategy); đăng ký lại thì thay nguồn cũ.</summary>
    public void RegisterCircuitStateSource(Func<IEnumerable<KeyValuePair<string, CircuitStateValue>>> source) =>
        Volatile.Write(ref _circuitStateSource, source ?? throw new ArgumentNullException(nameof(source)));

    /// <summary>Đăng ký nguồn trạng thái breaker của Redis (<c>null</c> thì gauge không phát số đo); đăng ký lại thì thay nguồn cũ.</summary>
    public void RegisterCacheCircuitStateSource(Func<CircuitStateValue?> source) =>
        Volatile.Write(ref _cacheCircuitStateSource, source ?? throw new ArgumentNullException(nameof(source)));

    /// <summary>Thay nguyên khối ảnh chụp số quyết định chờ duyệt theo chi nhánh (chi nhánh không còn trong ảnh chụp thì ngừng phát).</summary>
    public void ReplacePendingApprovals(IReadOnlyDictionary<Guid, long> countsByBranch) =>
        Volatile.Write(ref _pendingApprovals, new Dictionary<Guid, long>(countsByBranch ?? throw new ArgumentNullException(nameof(countsByBranch))));

    /// <inheritdoc />
    public void Dispose() => Meter.Dispose();

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    private IEnumerable<Measurement<int>> ObserveCircuitState()
    {
        var source = Volatile.Read(ref _circuitStateSource);
        return source is null
            ? []
            : source().Select(entry => new Measurement<int>((int)entry.Value, Tag(TadssMetricNames.LabelStrategy, entry.Key)));
    }

    private IEnumerable<Measurement<int>> ObserveCacheCircuitState()
    {
        var state = Volatile.Read(ref _cacheCircuitStateSource)?.Invoke();
        return state is { } value ? [new Measurement<int>((int)value)] : [];
    }

    private IEnumerable<Measurement<long>> ObservePendingApprovals() =>
        Volatile.Read(ref _pendingApprovals)
            .Select(entry => new Measurement<long>(entry.Value, Tag(TadssMetricNames.LabelBranch, entry.Key.ToString("D"))));
}
