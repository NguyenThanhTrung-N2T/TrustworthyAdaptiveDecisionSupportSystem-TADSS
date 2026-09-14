namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Trạng thái kết quả quyết định tự động từ TADSS Decision Engine.
/// </summary>
public enum DecisionStatus
{
    /// <summary>
    /// Đơn đặt sân được tự động chấp thuận.
    /// </summary>
    APPROVED = 0,

    /// <summary>
    /// Đơn đặt sân bị từ chối do vi phạm quy tắc hoặc xung đột lịch.
    /// </summary>
    REJECTED = 1,

    /// <summary>
    /// Đơn đặt sân cần xem xét và phê duyệt thủ công bởi Quản lý (Human-in-the-loop).
    /// </summary>
    NEED_APPROVAL = 2
}
