namespace Shared.DecisionContractSDK.V2.Common;

/// <summary>
/// Mã lý do của quyết định (trường <c>reasonCode</c>, tối đa 64 ký tự) — SOFTWARE_ARCHITECTURE_SPECIFICATION 4.3
/// và INTEGRATION_SPEC §1.1.
/// </summary>
public static class ReasonCodes
{
    /// <summary>Cọc theo hạng thành viên (<c>DefaultDecisionStrategy</c>).</summary>
    public const string TierDeposit = "TIER_DEPOSIT";

    /// <summary>Đơn giá trị lớn cần Manager duyệt.</summary>
    public const string HighValueReview = "HIGH_VALUE_REVIEW";

    /// <summary>Khách có lịch sử không đến cần Manager duyệt.</summary>
    public const string NoShowHistoryReview = "NO_SHOW_HISTORY_REVIEW";

    /// <summary>Cọc khung giờ cao điểm (<c>MockAlternativeDecisionStrategy</c>).</summary>
    public const string PeakHourDeposit = "PEAK_HOUR_DEPOSIT";

    /// <summary>Cọc ngoài giờ cao điểm (<c>MockAlternativeDecisionStrategy</c>).</summary>
    public const string OffPeakDeposit = "OFF_PEAK_DEPOSIT";

    /// <summary>Cần duyệt nhưng không còn đủ thời gian duyệt trước giờ chơi → cọc 100%.</summary>
    public const string ApprovalWindowUnavailable = "APPROVAL_WINDOW_UNAVAILABLE";

    /// <summary>Fallback do strategy vượt timeout.</summary>
    public const string FallbackStrategyTimeout = "FALLBACK_STRATEGY_TIMEOUT";

    /// <summary>Fallback do strategy ném lỗi.</summary>
    public const string FallbackStrategyException = "FALLBACK_STRATEGY_EXCEPTION";

    /// <summary>Fallback do circuit breaker đang mở.</summary>
    public const string FallbackCircuitOpen = "FALLBACK_CIRCUIT_OPEN";
}
