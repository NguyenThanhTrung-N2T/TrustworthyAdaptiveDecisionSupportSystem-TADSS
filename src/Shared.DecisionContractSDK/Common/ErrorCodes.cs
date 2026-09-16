namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Machine-readable error codes tập trung cho toàn bộ hệ sinh thái TADSS và SmashCourt.
/// </summary>
public static class ErrorCodes
{
    // ─── Success ─────────────────────────────────────────────────────────
    /// <summary>Xử lý thành công.</summary>
    public const string Success = "SUCCESS";

    // ─── Client errors (4xx) ─────────────────────────────────────────────
    /// <summary>Lỗi dữ liệu đầu vào không hợp lệ.</summary>
    public const string ValidationError         = "VALIDATION_ERROR";
    /// <summary>Yêu cầu không hợp lệ.</summary>
    public const string BadRequest              = "BAD_REQUEST";
    /// <summary>Chưa xác thực.</summary>
    public const string Unauthorized            = "UNAUTHORIZED";
    /// <summary>Không có quyền truy cập.</summary>
    public const string Forbidden               = "FORBIDDEN";
    /// <summary>Không tìm thấy tài nguyên.</summary>
    public const string NotFound                = "NOT_FOUND";
    /// <summary>Xung đột dữ liệu hoặc trạng thái.</summary>
    public const string Conflict                = "CONFLICT";

    // ─── Decision & Invariant Specific ───────────────────────────────────
    /// <summary>Thiếu header X-Idempotency-Key bắt buộc.</summary>
    public const string MissingIdempotencyKey   = "MISSING_IDEMPOTENCY_KEY";
    /// <summary>Idempotency key bị tái sử dụng với payload khác.</summary>
    public const string IdempotencyKeyReuse     = "IDEMPOTENCY_KEY_REUSE";
    /// <summary>Vi phạm bất biến nghiệp vụ Domain Invariant.</summary>
    public const string DomainInvariantViolation = "DOMAIN_INVARIANT_VIOLATION";
    /// <summary>Không tìm thấy bản ghi quyết định.</summary>
    public const string DecisionNotFound        = "DECISION_NOT_FOUND";
    /// <summary>Quyết định đã được can thiệp ghi đè trước đó.</summary>
    public const string DecisionAlreadyOverridden = "DECISION_ALREADY_OVERRIDDEN";
    /// <summary>Quyết định không đủ điều kiện để ghi đè.</summary>
    public const string DecisionNotEligibleForOverride = "DECISION_NOT_ELIGIBLE_FOR_OVERRIDE";
    /// <summary>Khoảng thời gian bắt đầu và kết thúc không hợp lệ.</summary>
    public const string InvalidScheduleRange    = "INVALID_SCHEDULE_RANGE";
    /// <summary>Thời lượng đặt sân nằm ngoài giới hạn 30 phút - 12 giờ.</summary>
    public const string InvalidScheduleDuration = "INVALID_SCHEDULE_DURATION";
    /// <summary>Tiền cọc vượt quá tổng tiền.</summary>
    public const string DepositExceedsTotal     = "DEPOSIT_EXCEEDS_TOTAL_PRICE";

    // ─── Strategy Reason Codes ───────────────────────────────────────────
    /// <summary>Đã thanh toán đủ 100%.</summary>
    public const string ApprovedPaidInFull      = "APPROVED_PAID_IN_FULL";
    /// <summary>Duyệt tự động theo quy chuẩn tiêu chuẩn.</summary>
    public const string ApprovedStandard        = "APPROVED_STANDARD";
    /// <summary>Tự động duyệt dựa trên phân hạng khách hàng.</summary>
    public const string AutoApprovedTierEligible = "AUTO_APPROVED_TIER_ELIGIBLE";
    /// <summary>Xung đột khung giờ đặt sân.</summary>
    public const string SlotConflict            = "SLOT_CONFLICT";
    /// <summary>Tiền cọc dưới ngưỡng quy định.</summary>
    public const string DepositBelowThreshold   = "DEPOSIT_BELOW_THRESHOLD";
    /// <summary>Cần quản lý duyệt thủ công do giá trị đơn lớn.</summary>
    public const string ManualReviewHighValue   = "MANUAL_REVIEW_HIGH_VALUE";
    /// <summary>Fallback do timeout chiến lược.</summary>
    public const string FallbackServiceTimeout  = "FALLBACK_SERVICE_TIMEOUT";
    /// <summary>Fallback do chạm deadline ngoài.</summary>
    public const string FallbackOuterDeadline   = "FALLBACK_OUTER_DEADLINE";
    /// <summary>Fallback an toàn.</summary>
    public const string FallbackSafetyMode      = "FALLBACK_SAFETY_MODE";
    /// <summary>Fallback do lỗi thực thi chiến lược.</summary>
    public const string FallbackExecutionError  = "FALLBACK_EXECUTION_ERROR";
    /// <summary>Đã được can thiệp ghi đè bởi Quản lý.</summary>
    public const string OverriddenByManager     = "OVERRIDDEN_BY_MANAGER";

    // ─── Server & Resilience errors (5xx) ────────────────────────────────
    /// <summary>Timeout kết nối gateway / engine.</summary>
    public const string GatewayTimeout          = "GATEWAY_TIMEOUT";
    /// <summary>Lỗi máy chủ nội bộ.</summary>
    public const string InternalError           = "INTERNAL_ERROR";
}
