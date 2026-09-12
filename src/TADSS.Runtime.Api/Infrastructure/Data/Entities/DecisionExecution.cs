namespace TADSS.Runtime.Api.Infrastructure.Data.Entities;

/// <summary>
/// Đại diện cho bản ghi kiểm toán thực thi quyết định tại bảng tadss.decision_executions.
/// Toàn bộ timestamp được quản lý tập trung bởi AuditTimestampInterceptor.
/// </summary>
public class DecisionExecution
{
    // ── 1. Khóa định danh ──────────────────────────────────────────
    public Guid Id { get; set; }
    public Guid DecisionId { get; set; }
    public Guid ReservationId { get; set; }

    // ── 2. Thông tin Chiến lược ────────────────────────────────────
    public string StrategyName { get; set; } = string.Empty;
    public string StrategyVersion { get; set; } = "1.0.0";

    // ── 3. Thông số Telemetry & Thực thi ───────────────────────────
    public string ExecutionType { get; set; } = string.Empty; // StrategyExecution | CacheHit | FallbackExecution
    public int ExecutionTimeMs { get; set; }
    public bool FallbackUsed { get; set; }
    public string? FailureReason { get; set; }
    public bool CacheHit { get; set; }
    public bool NeedApproval { get; set; }

    // ── 4. Dữ liệu Request & Response Payload ──────────────────────
    public string RequestHash { get; set; } = string.Empty;
    public string ResponsePayload { get; set; } = "{}"; // JSONB chứa DecisionResult

    // ── 5. Thông tin Ghi đè của Quản lý (Human-in-the-loop) ─────────
    public bool IsOverridden { get; set; }
    public string? OverriddenBy { get; set; }
    public string? OverrideReason { get; set; }

    // ── 6. Idempotency, Concurrency & Audit Timestamps ─────────────
    public string? IdempotencyKey { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
