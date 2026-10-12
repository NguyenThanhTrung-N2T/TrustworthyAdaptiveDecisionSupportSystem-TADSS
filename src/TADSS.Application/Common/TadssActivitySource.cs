using System.Diagnostics;

namespace TADSS.Application.Common;

/// <summary>
/// Nơi <b>duy nhất</b> khai báo <see cref="ActivitySource"/>, tên span và tên thuộc tính của trace (O-07). Chỉ dùng BCL (<c>System.Diagnostics</c>)
/// nên Application và Infrastructure tạo span mà không phụ thuộc OpenTelemetry; composition root gắn exporter qua <c>AddSource(SourceName)</c>.
/// Không có listener thì <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> trả <c>null</c>, nên mã gọi luôn dùng <c>activity?.</c>.
/// <para>
/// Thuộc tính <b>không</b> được chứa khóa API, mã băm, chuỗi kết nối, khóa cache hay dữ liệu khách; span lỗi chỉ ghi <b>kiểu</b> exception (<see cref="MarkError"/>).
/// </para>
/// </summary>
public static class TadssActivitySource
{
    /// <summary>Tên nguồn trace (trùng tên meter).</summary>
    public const string SourceName = "TADSS.Runtime";

    // ── Tên span ─────────────────────────────────────────────────────────────────

    /// <summary>Span quanh <c>DecisionService.EvaluateAsync</c>.</summary>
    public const string DecisionEvaluate = "tadss.decision.evaluate";

    /// <summary>Span mỗi lần chạy pipeline của một strategy.</summary>
    public const string StrategyExecute = "tadss.strategy.execute";

    /// <summary>Span một việc shadow (gốc trace mới, có link tới request gốc).</summary>
    public const string ShadowExecute = "tadss.shadow.execute";

    /// <summary>Span quanh mỗi lệnh Redis của cache.</summary>
    public const string RedisCommand = "tadss.redis.command";

    // ── Tên thuộc tính ───────────────────────────────────────────────────────────

    /// <summary>Tên strategy.</summary>
    public const string TagStrategy = "tadss.strategy";

    /// <summary>Rule định tuyến đã chọn (rỗng khi dùng strategy mặc định).</summary>
    public const string TagRoutingRule = "tadss.routing_rule";

    /// <summary>Trạng thái quyết định (<c>APPROVED</c>/<c>NEED_APPROVAL</c>).</summary>
    public const string TagStatus = "tadss.status";

    /// <summary>Loại thực thi (<c>AUTOMATED</c>/<c>FALLBACK</c>).</summary>
    public const string TagExecutionType = "tadss.execution_type";

    /// <summary>Kết quả đến từ cache hay không.</summary>
    public const string TagCacheHit = "tadss.cache_hit";

    /// <summary>Correlation id của contract (khác W3C trace id; để tra chéo log ↔ trace ↔ <c>traceId</c> của problem+json).</summary>
    public const string TagCorrelationId = "tadss.correlation_id";

    /// <summary>Kết quả pipeline: <c>automated</c>/<c>fallback</c>/<c>timeout</c> (giá trị <c>PipelineOutcome*</c> của <see cref="TadssMetricNames"/>).</summary>
    public const string TagOutcome = "tadss.outcome";

    /// <summary>Lý do fallback (giá trị <c>FallbackReason*</c> của <see cref="TadssMetricNames"/>).</summary>
    public const string TagFallbackReason = "tadss.fallback_reason";

    /// <summary>Kết quả lệnh cache Redis (<c>Hit</c>/<c>Miss</c>/<c>Written</c>/<c>Failed</c>/<c>Bypassed</c>).</summary>
    public const string TagCacheResult = "tadss.cache.result";

    /// <summary><c>ClientId</c> của API key đã xác thực (gắn lên span server của ASP.NET Core).</summary>
    public const string TagClientId = "tadss.client_id";

    /// <summary>Hệ cơ sở dữ liệu theo quy ước của OpenTelemetry (<c>db.system</c>).</summary>
    public const string TagDbSystem = "db.system";

    /// <summary>Thao tác cơ sở dữ liệu theo quy ước của OpenTelemetry (<c>db.operation</c>).</summary>
    public const string TagDbOperation = "db.operation";

    // ── Giá trị thuộc tính ───────────────────────────────────────────────────────

    /// <summary>Giá trị <see cref="TagDbSystem"/> của Redis.</summary>
    public const string DbSystemRedis = "redis";

    /// <summary>Giá trị <see cref="TagDbOperation"/> khi đọc.</summary>
    public const string DbOperationGet = "GET";

    /// <summary>Giá trị <see cref="TagDbOperation"/> khi ghi.</summary>
    public const string DbOperationSet = "SET";

    /// <summary>Nguồn trace của runtime.</summary>
    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>
    /// Đánh dấu span lỗi: <see cref="ActivityStatusCode.Error"/> với <b>kiểu</b> exception làm mô tả (message có thể chứa chi tiết nội bộ nên không ghi).
    /// Không làm gì khi <paramref name="activity"/> là <c>null</c> (không có listener).
    /// </summary>
    public static void MarkError(Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
    }
}
