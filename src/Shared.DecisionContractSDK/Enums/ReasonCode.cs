using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Alias tham chiếu tới các mã lý do chuẩn hóa trong ErrorCodes.
/// </summary>
public static class ReasonCode
{
    public const string ApprovedPaidInFull      = ErrorCodes.ApprovedPaidInFull;
    public const string ApprovedStandard        = ErrorCodes.ApprovedStandard;
    public const string AutoApprovedTierEligible = ErrorCodes.AutoApprovedTierEligible;
    public const string SlotConflict            = ErrorCodes.SlotConflict;
    public const string DepositBelowThreshold   = ErrorCodes.DepositBelowThreshold;
    public const string ManualReviewHighValue   = ErrorCodes.ManualReviewHighValue;
    public const string FallbackServiceTimeout  = ErrorCodes.FallbackServiceTimeout;
    public const string FallbackSafetyMode      = ErrorCodes.FallbackSafetyMode;
    public const string FallbackExecutionError  = ErrorCodes.FallbackExecutionError;
    public const string OverriddenByManager     = ErrorCodes.OverriddenByManager;
}
