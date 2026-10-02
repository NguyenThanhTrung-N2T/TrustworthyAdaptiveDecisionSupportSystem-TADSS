namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Mã lỗi của contract v2, trả trong trường <c>code</c> của RFC 7807 (INTEGRATION_SPEC §3.8).
/// </summary>
public static class ErrorCodes
{
    /// <summary>Payload sai cấu trúc, sai định dạng, enum lạ, trường thừa hoặc thiếu trường bắt buộc (400).</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>Payload đúng định dạng nhưng vi phạm bất biến domain (400).</summary>
    public const string DomainInvariantViolation = "DOMAIN_INVARIANT_VIOLATION";

    /// <summary>Thiếu header bắt buộc (400).</summary>
    public const string MissingHeader = "MISSING_HEADER";

    /// <summary>Thiếu hoặc sai <c>X-Api-Key</c> (401).</summary>
    public const string Unauthorized = "UNAUTHORIZED";

    /// <summary>Không có <c>decisionId</c> (404).</summary>
    public const string DecisionNotFound = "DECISION_NOT_FOUND";

    /// <summary>Dùng lại idempotency key với payload khác (409).</summary>
    public const string IdempotencyKeyReuse = "IDEMPOTENCY_KEY_REUSE";

    /// <summary>Quyết định không ở trạng thái chờ duyệt (409).</summary>
    public const string NotPendingApproval = "NOT_PENDING_APPROVAL";

    /// <summary>Quyết định đã được override (409).</summary>
    public const string AlreadyOverridden = "ALREADY_OVERRIDDEN";

    /// <summary>Đã quá hạn duyệt (409).</summary>
    public const string ApprovalExpired = "APPROVAL_EXPIRED";

    /// <summary>Quyết định đã có outcome khác (409).</summary>
    public const string OutcomeAlreadyRecorded = "OUTCOME_ALREADY_RECORDED";

    /// <summary>Không ghi được audit, fail-closed (503).</summary>
    public const string PersistenceUnavailable = "PERSISTENCE_UNAVAILABLE";

    /// <summary>Vượt deadline toàn cục (504).</summary>
    public const string GatewayTimeout = "GATEWAY_TIMEOUT";

    /// <summary>Lỗi không mong đợi, ẩn chi tiết kỹ thuật (500).</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
