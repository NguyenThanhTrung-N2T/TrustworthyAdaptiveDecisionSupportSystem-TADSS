namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Định nghĩa mức độ ưu tiên của yêu cầu thẩm định đặt sân trong ngữ cảnh quyết định của TADSS Engine.
/// LƯU Ý KIẾN TRÚC: BookingPriority đại diện cho mức ưu tiên của phiên đặt chỗ hiện tại (Request Context),
/// hoàn toàn độc lập với hạng thân thiết/khách hàng (CustomerTier do phía Client quản lý).
/// </summary>
public enum BookingPriority
{
    /// <summary>
    /// Đơn đặt sân thông thường (Mặc định).
    /// </summary>
    NORMAL = 0,

    /// <summary>
    /// Đơn ưu tiên (Sự kiện đặt sân ưu tiên cao, trận đấu quan trọng).
    /// </summary>
    HIGH = 1,

    /// <summary>
    /// Đơn VIP (Giải đấu đặc biệt, phiên đặt sân tiêu chuẩn VIP).
    /// </summary>
    VIP = 2,

    /// <summary>
    /// Đơn khẩn cấp / Giờ chót cần phản hồi tức thì.
    /// </summary>
    URGENT = 3
}
