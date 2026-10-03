using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Decisions;

namespace TADSS.Infrastructure.Persistence.Entities;

/// <summary>
/// Bản ghi audit bất biến của một lần đánh giá quyết định, ánh xạ 1-1 với <c>tadss.decision_executions</c>
/// (DECISION_PERSISTENCE_INVARIANTS B.1). Bảng append-only (trigger <c>trg_de_append_only</c>), nên entity không có
/// <c>updated_at</c>, <c>version</c> hay <c>is_overridden</c>; thời gian do <c>DecisionService</c> gán (UTC, offset 0).
/// </summary>
public class DecisionExecution
{
    // ── 1. Khóa định danh ──────────────────────────────────────────
    /// <summary>Khóa kỹ thuật, do cơ sở dữ liệu sinh (<c>gen_random_uuid()</c>).</summary>
    public Guid Id { get; set; }

    /// <summary>Định danh logic của lần đánh giá (unique — INV-1).</summary>
    public Guid DecisionId { get; set; }

    /// <summary>Định danh đơn phía client.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Định danh chi nhánh (lọc hàng chờ duyệt).</summary>
    public Guid BranchId { get; set; }

    /// <summary>Vai trò bản ghi: <c>PRIMARY</c> hoặc <c>SHADOW</c>.</summary>
    public ExecutionRole Role { get; set; }

    // ── 2. Chiến lược và kết quả ───────────────────────────────────
    /// <summary>Tên strategy đã được chọn.</summary>
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>Phiên bản strategy đã được chọn.</summary>
    public string StrategyVersion { get; set; } = string.Empty;

    /// <summary>Kết quả gốc của strategy/fallback, trước khi áp approval window.</summary>
    public DecisionStatus StrategyStatus { get; set; }

    /// <summary>Trạng thái cuối cùng trả cho client.</summary>
    public DecisionStatus Status { get; set; }

    /// <summary>Tỷ lệ cọc; chỉ có khi <see cref="Status"/> là <c>APPROVED</c>.</summary>
    public decimal? DepositRate { get; set; }

    /// <summary>Số tiền cọc; chỉ có khi <see cref="Status"/> là <c>APPROVED</c>.</summary>
    public decimal? DepositAmount { get; set; }

    /// <summary>Mã lý do cuối cùng.</summary>
    public string ReasonCode { get; set; } = string.Empty;

    // ── 3. Telemetry và thực thi ───────────────────────────────────
    /// <summary>Cách quyết định được tạo ra.</summary>
    public ExecutionType ExecutionType { get; set; }

    /// <summary>Thời gian chạy strategy (ms, không âm).</summary>
    public int ExecutionTimeMs { get; set; }

    /// <summary>Có dùng fallback hay không.</summary>
    public bool FallbackUsed { get; set; }

    /// <summary>Lý do máy đọc được khi fallback.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Quyết định của strategy lấy từ cache.</summary>
    public bool CacheHit { get; set; }

    /// <summary>Hạn duyệt; chỉ có khi <see cref="Status"/> là <c>NEED_APPROVAL</c>.</summary>
    public DateTimeOffset? ApprovalExpiresAt { get; set; }

    // ── 4. Request/response và định danh yêu cầu ───────────────────
    /// <summary>SHA-256 hex thường (64 ký tự) của request đã chuẩn hóa.</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>Request v2 đã chuẩn hóa (cột <c>jsonb</c>).</summary>
    public string RequestPayload { get; set; } = "{}";

    /// <summary>Response v2 (cột <c>jsonb</c>).</summary>
    public string ResponsePayload { get; set; } = "{}";

    /// <summary>Khóa idempotency.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Mã tương quan của request.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Thời điểm đánh giá (UTC, offset 0).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
