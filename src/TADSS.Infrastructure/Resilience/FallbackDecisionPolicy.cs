using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;

namespace TADSS.Infrastructure.Resilience;

/// <summary>
/// Tạo quyết định dự phòng của TADSS khi strategy không dùng được (timeout, lỗi, circuit mở). Luôn <c>APPROVED</c>
/// với tỷ lệ cọc 100% (<see cref="FullDepositRate"/>) và không kèm khuyến nghị; thuần túy, không I/O.
/// <para>
/// Mọi sự cố kỹ thuật dùng chung một quy tắc cọc 100% (giống khi hệ thống đặt sân tắt TADSS hoặc không gọi được TADSS);
/// <c>NEED_APPROVAL</c> chỉ dành cho bất định nghiệp vụ do strategy quyết định. Vì vậy quyết định fallback không vào
/// hàng chờ duyệt và không thể override (override chỉ áp cho <c>NEED_APPROVAL</c>).
/// </para>
/// </summary>
public static class FallbackDecisionPolicy
{
    /// <summary>Tỷ lệ cọc của quyết định fallback: cọc toàn bộ giá trị đơn.</summary>
    public const decimal FullDepositRate = 1m;

    private const string FullDepositReason = "Áp dụng đặt cọc 100% theo chính sách an toàn.";

    /// <summary>Tạo quyết định fallback cho <paramref name="cause"/>.</summary>
    /// <param name="cause">Nguyên nhân fallback.</param>
    /// <exception cref="ArgumentOutOfRangeException">Khi <paramref name="cause"/> không phải giá trị đã định nghĩa.</exception>
    public static StrategyDecision Create(FallbackCause cause)
    {
        var (reasonCode, detail) = cause switch
        {
            FallbackCause.Timeout => (
                ReasonCodes.FallbackStrategyTimeout,
                "Chiến lược đánh giá không phản hồi kịp thời hạn cho phép."),
            FallbackCause.Exception => (
                ReasonCodes.FallbackStrategyException,
                "Chiến lược đánh giá gặp lỗi trong quá trình xử lý."),
            FallbackCause.CircuitOpen => (
                ReasonCodes.FallbackCircuitOpen,
                "Chiến lược đánh giá đang tạm ngưng do liên tục gặp sự cố."),
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Nguyên nhân fallback không hợp lệ.")
        };

        return StrategyDecision.Approve(
            FullDepositRate,
            reasonCode,
            ["Chế độ an toàn tự động kích hoạt: " + detail, FullDepositReason]);
    }
}
