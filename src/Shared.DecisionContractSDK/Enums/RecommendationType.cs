namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Phân loại giải pháp / đề xuất tối ưu khi đơn đặt sân không được duyệt tự động.
/// </summary>
public enum RecommendationType
{
    /// <summary>
    /// Gợi ý khung giờ khác còn trống.
    /// </summary>
    ALTERNATIVE_SLOT = 0,

    /// <summary>
    /// Gợi ý chuyển sang sân khác cùng cơ sở.
    /// </summary>
    ALTERNATIVE_COURT = 1,

    /// <summary>
    /// Gợi ý điều chỉnh giá hoặc áp dụng khuyến mãi kích cầu.
    /// </summary>
    PRICE_ADJUSTMENT = 2,

    /// <summary>
    /// Yêu cầu thanh toán bổ sung tiền cọc để đủ điều kiện duyệt.
    /// </summary>
    REQUIRE_DEPOSIT = 3,

    /// <summary>
    /// Yêu cầu chuyển lên cấp Quản lý xem xét duyệt thủ công.
    /// </summary>
    REQUIRE_APPROVAL = 4,

    /// <summary>
    /// Nâng cấp mức độ ưu tiên xử lý đơn.
    /// </summary>
    PRIORITY_ESCALATION = 5
}
