namespace Shared.DecisionContractSDK.Common;

/// <summary>
/// Machine-readable error codes tập trung cho toàn bộ hệ thống TADSS & SmashCourt.
/// </summary>
public static class ErrorCodes
{
    // ─── Success ─────────────────────────────────────────────────────────
    public const string Success = "SUCCESS";

    // ─── Client errors (4xx) ─────────────────────────────────────────────
    public const string ValidationError         = "VALIDATION_ERROR";
    public const string BadRequest              = "BAD_REQUEST";
    public const string Unauthorized            = "UNAUTHORIZED";
    public const string Forbidden               = "FORBIDDEN";
    public const string NotFound                = "NOT_FOUND";
    public const string Conflict                = "CONFLICT";

    // ─── Decision & Invariant Specific ───────────────────────────────────
    public const string MissingIdempotencyKey   = "MISSING_IDEMPOTENCY_KEY";
    public const string IdempotencyKeyReuse     = "IDEMPOTENCY_KEY_REUSE";
    public const string DomainInvariantViolation = "DOMAIN_INVARIANT_VIOLATION";
    public const string DecisionNotFound        = "DECISION_NOT_FOUND";
    public const string DecisionAlreadyOverridden = "DECISION_ALREADY_OVERRIDDEN";
    public const string DecisionNotEligibleForOverride = "DECISION_NOT_ELIGIBLE_FOR_OVERRIDE";
    public const string InvalidScheduleRange    = "INVALID_SCHEDULE_RANGE";
    public const string InvalidScheduleDuration = "INVALID_SCHEDULE_DURATION";
    public const string DepositExceedsTotal     = "DEPOSIT_EXCEEDS_TOTAL_PRICE";

    // ─── Strategy Reason Codes ───────────────────────────────────────────
    public const string ApprovedPaidInFull      = "APPROVED_PAID_IN_FULL";
    public const string ApprovedStandard        = "APPROVED_STANDARD";
    public const string AutoApprovedTierEligible = "AUTO_APPROVED_TIER_ELIGIBLE";
    public const string SlotConflict            = "SLOT_CONFLICT";
    public const string DepositBelowThreshold   = "DEPOSIT_BELOW_THRESHOLD";
    public const string ManualReviewHighValue   = "MANUAL_REVIEW_HIGH_VALUE";
    public const string FallbackServiceTimeout  = "FALLBACK_SERVICE_TIMEOUT";
    public const string FallbackOuterDeadline   = "FALLBACK_OUTER_DEADLINE";
    public const string FallbackSafetyMode      = "FALLBACK_SAFETY_MODE";
    public const string FallbackExecutionError  = "FALLBACK_EXECUTION_ERROR";
    public const string OverriddenByManager     = "OVERRIDDEN_BY_MANAGER";

    // ─── Server & Resilience errors (5xx) ────────────────────────────────
    public const string GatewayTimeout          = "GATEWAY_TIMEOUT";
    public const string InternalError           = "INTERNAL_ERROR";
}
