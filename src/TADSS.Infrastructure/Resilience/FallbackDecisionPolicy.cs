using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace TADSS.Infrastructure.Resilience;

/// <summary>
/// Tạo quyết định dự phòng của TADSS khi strategy không dùng được. Luôn <c>NEED_APPROVAL</c> với một khuyến nghị
/// <c>REQUIRE_APPROVAL</c>; thuần túy, không I/O. Mức cọc khi cần duyệt hoặc khi TADSS lỗi do hệ thống đặt sân
/// quyết định (chính sách của SmashCourt), nên lý do ở đây không nêu con số cọc.
/// </summary>
public static class FallbackDecisionPolicy
{
    private const string RecommendationDescription =
        "Yêu cầu Quản lý chi nhánh kiểm tra và duyệt đơn thủ công.";

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

        return StrategyDecision.RequireApproval(
            reasonCode,
            [
                "Chế độ an toàn tự động kích hoạt: " + detail,
                "Đơn được chuyển sang trạng thái cần duyệt thủ công."
            ],
            [new DecisionRecommendation(RecommendationType.REQUIRE_APPROVAL, RecommendationDescription)]);
    }
}
