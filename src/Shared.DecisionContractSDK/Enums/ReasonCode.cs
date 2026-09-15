using Shared.DecisionContractSDK.Common;

namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Alias tham chiếu tới các mã lý do chuẩn hóa trong ErrorCodes.
/// </summary>
public static class ReasonCode
{
    /// <summary>Đã thanh toán đủ 100%.</summary>
    public const string ApprovedPaidInFull      = ErrorCodes.ApprovedPaidInFull;
    /// <summary>Duyệt tự động theo quy chuẩn tiêu chuẩn.</summary>
    public const string ApprovedStandard        = ErrorCodes.ApprovedStandard;
    /// <summary>Tự động duyệt dựa trên phân hạng khách hàng.</summary>
    public const string AutoApprovedTierEligible = ErrorCodes.AutoApprovedTierEligible;
    /// <summary>Xung đột khung giờ đặt sân.</summary>
    public const string SlotConflict            = ErrorCodes.SlotConflict;
    /// <summary>Tiền cọc dưới ngưỡng quy định.</summary>
    public const string DepositBelowThreshold   = ErrorCodes.DepositBelowThreshold;
    /// <summary>Cần quản lý duyệt thủ công do giá trị đơn lớn.</summary>
    public const string ManualReviewHighValue   = ErrorCodes.ManualReviewHighValue;
    /// <summary>Fallback do timeout chiến lược.</summary>
    public const string FallbackServiceTimeout  = ErrorCodes.FallbackServiceTimeout;
    /// <summary>Fallback an toàn.</summary>
    public const string FallbackSafetyMode      = ErrorCodes.FallbackSafetyMode;
    /// <summary>Fallback do lỗi thực thi chiến lược.</summary>
    public const string FallbackExecutionError  = ErrorCodes.FallbackExecutionError;
    /// <summary>Đã được can thiệp ghi đè bởi Quản lý.</summary>
    public const string OverriddenByManager     = ErrorCodes.OverriddenByManager;
}
