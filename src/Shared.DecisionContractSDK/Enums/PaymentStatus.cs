namespace Shared.DecisionContractSDK.Enums;

/// <summary>
/// Trạng thái thanh toán của phiên đặt sân.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Chưa thanh toán tiền cọc hoặc tổng tiền.
    /// </summary>
    UNPAID = 0,

    /// <summary>
    /// Đã thanh toán một phần tiền cọc.
    /// </summary>
    PARTIALLY_PAID = 1,

    /// <summary>
    /// Đã thanh toán 100% chi phí đặt sân.
    /// </summary>
    PAID = 2,

    /// <summary>
    /// Đã hoàn tiền cho khách hàng.
    /// </summary>
    REFUNDED = 3,

    /// <summary>
    /// Phiên thanh toán / hóa đơn đã hết hạn.
    /// </summary>
    EXPIRED = 4
}
