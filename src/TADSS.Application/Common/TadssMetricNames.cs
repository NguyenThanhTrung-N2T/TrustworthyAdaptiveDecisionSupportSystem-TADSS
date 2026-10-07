namespace TADSS.Application.Common;

/// <summary>
/// Nơi <b>duy nhất</b> khai báo tên meter, tên instrument, tên nhãn, giá trị nhãn và biên histogram của runtime (A-10, O-05).
/// Mã khác chỉ tham chiếu hằng ở đây, không viết literal tên metric; dashboard Grafana chỉ được tham chiếu metric có trong
/// <see cref="AllInstrumentNames"/> (OB-1).
/// </summary>
public static class TadssMetricNames
{
    /// <summary>Tên meter của runtime.</summary>
    public const string MeterName = "TADSS.Runtime";

    /// <summary>Đơn vị của mọi histogram thời gian.</summary>
    public const string UnitMilliseconds = "ms";

    // ── Instrument ───────────────────────────────────────────────────────────────

    /// <summary>Counter: số lần nạp lại cấu hình thất bại (giữ cấu hình cũ), nhãn <see cref="LabelSection"/>.</summary>
    public const string ConfigReloadFailures = "tadss.config.reload_failures";

    /// <summary>Counter: số lượt chạy strategy bị bỏ rơi vì token hủy khi strategy chưa xong, nhãn <see cref="LabelStrategy"/>.</summary>
    public const string StrategyAbandoned = "tadss.strategy.abandoned";

    /// <summary>Counter: số lần tra cache strategy, nhãn <see cref="LabelResult"/> (<see cref="CacheResultHit"/>, <see cref="CacheResultMiss"/>, <see cref="CacheResultBypass"/>).</summary>
    public const string CacheRequests = "tadss.cache.requests";

    /// <summary>Counter: số việc shadow bị bỏ vì hàng đợi đầy, nhãn <see cref="LabelReason"/>.</summary>
    public const string ShadowDropped = "tadss.shadow.dropped";

    /// <summary>Counter: số lần chạy shadow, nhãn <see cref="LabelOutcome"/> (<see cref="ShadowOutcomeRecorded"/>, <see cref="ShadowOutcomeFailed"/>).</summary>
    public const string ShadowExecutions = "tadss.shadow.executions";

    /// <summary>Counter: mỗi bản <c>PRIMARY</c> mới của chính request, nhãn <see cref="LabelStatus"/>, <see cref="LabelStrategy"/>, <see cref="LabelExecutionType"/>.</summary>
    public const string Decisions = "tadss.decisions";

    /// <summary>Histogram (ms): thời gian xử lý mỗi request <c>/api/*</c>, nhãn <see cref="LabelEndpoint"/>, <see cref="LabelStatusCode"/>.</summary>
    public const string DecisionDuration = "tadss.decision.duration";

    /// <summary>Histogram (ms): thời gian pipeline strategy, nhãn <see cref="LabelStrategy"/>, <see cref="LabelOutcome"/> (<c>PipelineOutcome*</c>).</summary>
    public const string PipelineDuration = "tadss.pipeline.duration";

    /// <summary>Counter: số quyết định fallback, nhãn <see cref="LabelReason"/> (<c>FallbackReason*</c>), <see cref="LabelStrategy"/>.</summary>
    public const string Fallbacks = "tadss.fallbacks";

    /// <summary>Gauge: trạng thái circuit breaker của từng strategy (<see cref="CircuitStateValue"/>), nhãn <see cref="LabelStrategy"/>.</summary>
    public const string CircuitState = "tadss.circuit.state";

    /// <summary>Gauge: trạng thái circuit breaker của Redis (<see cref="CircuitStateValue"/>), không nhãn; chỉ phát khi dùng Redis.</summary>
    public const string CacheCircuitState = "tadss.cache.circuit.state";

    /// <summary>Gauge: số quyết định đang chờ duyệt, nhãn <see cref="LabelBranch"/>.</summary>
    public const string PendingApprovals = "tadss.pending_approvals";

    /// <summary>Counter: số override mới được ghi, nhãn <see cref="LabelAction"/>.</summary>
    public const string Overrides = "tadss.overrides";

    // ── Tên nhãn ─────────────────────────────────────────────────────────────────

    /// <summary>Nhãn <c>section</c>.</summary>
    public const string LabelSection = "section";

    /// <summary>Nhãn <c>strategy</c>.</summary>
    public const string LabelStrategy = "strategy";

    /// <summary>Nhãn <c>result</c>.</summary>
    public const string LabelResult = "result";

    /// <summary>Nhãn <c>reason</c>.</summary>
    public const string LabelReason = "reason";

    /// <summary>Nhãn <c>outcome</c>.</summary>
    public const string LabelOutcome = "outcome";

    /// <summary>Nhãn <c>status</c>.</summary>
    public const string LabelStatus = "status";

    /// <summary>Nhãn <c>execution_type</c>.</summary>
    public const string LabelExecutionType = "execution_type";

    /// <summary>Nhãn <c>endpoint</c>.</summary>
    public const string LabelEndpoint = "endpoint";

    /// <summary>Nhãn <c>status_code</c>.</summary>
    public const string LabelStatusCode = "status_code";

    /// <summary>Nhãn <c>branch</c>.</summary>
    public const string LabelBranch = "branch";

    /// <summary>Nhãn <c>action</c>.</summary>
    public const string LabelAction = "action";

    // ── Giá trị nhãn ─────────────────────────────────────────────────────────────

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

    /// <summary>Pipeline trả kết quả của strategy.</summary>
    public const string PipelineOutcomeAutomated = "automated";

    /// <summary>Pipeline trả quyết định fallback.</summary>
    public const string PipelineOutcomeFallback = "fallback";

    /// <summary>Pipeline vượt outer timeout (<c>DecisionTimeoutException</c>).</summary>
    public const string PipelineOutcomeTimeout = "timeout";

    /// <summary>Fallback vì strategy vượt inner timeout.</summary>
    public const string FallbackReasonTimeout = "timeout";

    /// <summary>Fallback vì strategy ném lỗi.</summary>
    public const string FallbackReasonException = "exception";

    /// <summary>Fallback vì circuit breaker của strategy đang mở.</summary>
    public const string FallbackReasonCircuitOpen = "circuit_open";

    /// <summary>Đuôi nhãn <c>endpoint</c> khi request bị từ chối trước routing (401/413/429): <c>"{METHOD} unmatched"</c>.</summary>
    public const string EndpointUnmatched = "unmatched";

    /// <summary>
    /// Tên mọi instrument của meter; dùng để đối chiếu dashboard (OB-1) và kiểm tra tập metric thực sự được phát.
    /// </summary>
    public static IReadOnlyList<string> AllInstrumentNames { get; } =
    [
        ConfigReloadFailures,
        StrategyAbandoned,
        CacheRequests,
        ShadowDropped,
        ShadowExecutions,
        Decisions,
        DecisionDuration,
        PipelineDuration,
        Fallbacks,
        CircuitState,
        CacheCircuitState,
        PendingApprovals,
        Overrides,
    ];

    /// <summary>
    /// Biên bucket (ms) của mọi histogram thời gian. Có 150, 500 và 700 trùng ngưỡng KPI (SC1, SC2) nên "tỉ lệ request ≤ ngưỡng"
    /// là phép đếm chính xác trên bucket, không phải nội suy phân vị.
    /// </summary>
    public static IReadOnlyList<double> DurationBucketBoundariesMs { get; } =
        [5, 10, 25, 50, 75, 100, 150, 200, 300, 500, 700, 1000, 2500, 5000];
}
