using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Yêu cầu thẩm định quyết định đặt sân gửi từ SmashCourt-BE sang TADSS Engine.
/// </summary>
public class DecisionRequestDto
{
    /// <summary>
    /// Định danh duy nhất của đơn đặt sân.
    /// </summary>
    [Required(ErrorMessage = "ReservationId không được để trống")]
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Định danh sân cụ thể được yêu cầu đặt.
    /// </summary>
    [Required(ErrorMessage = "CourtId không được để trống")]
    [JsonPropertyName("courtId")]
    public Guid CourtId { get; set; }

    /// <summary>
    /// Định danh loại sân (tùy chọn).
    /// </summary>
    [JsonPropertyName("courtTypeId")]
    public Guid? CourtTypeId { get; set; }

    /// <summary>
    /// Định danh chi nhánh sở hữu sân.
    /// </summary>
    [Required(ErrorMessage = "BranchId không được để trống")]
    [JsonPropertyName("branchId")]
    public Guid BranchId { get; set; }

    /// <summary>
    /// Định danh khách hàng thực hiện đặt sân.
    /// </summary>
    [Required(ErrorMessage = "CustomerId không được để trống")]
    [JsonPropertyName("customerId")]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Phân hạng thân thiết của khách hàng (STANDARD, SILVER, GOLD, VIP, DIAMOND,...).
    /// </summary>
    [JsonPropertyName("customerTier")]
    public string? CustomerTier { get; set; } = "STANDARD";

    /// <summary>
    /// Thời điểm bắt đầu phiên đặt sân (UTC).
    /// </summary>
    [Required(ErrorMessage = "StartTime không được để trống")]
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Thời điểm kết thúc phiên đặt sân (UTC).
    /// </summary>
    [Required(ErrorMessage = "EndTime không được để trống")]
    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Loại hình đặt sân (SINGLE, RECURRING, EVENT).
    /// </summary>
    [Required(ErrorMessage = "BookingType không được để trống")]
    [JsonPropertyName("bookingType")]
    public string BookingType { get; set; } = "SINGLE";

    /// <summary>
    /// Tổng tiền thuê sân (VND, &gt;= 0).
    /// </summary>
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "TotalPrice phải lớn hơn hoặc bằng 0")]
    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; set; }

    /// <summary>
    /// Số tiền cọc đã trả (VND, 0 &lt;= depositAmount &lt;= totalPrice).
    /// </summary>
    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "DepositAmount phải lớn hơn hoặc bằng 0")]
    [JsonPropertyName("depositAmount")]
    public decimal DepositAmount { get; set; }

    /// <summary>
    /// Trạng thái thanh toán (UNPAID, PARTIALLY_PAID, PAID, REFUNDED, EXPIRED).
    /// </summary>
    [Required(ErrorMessage = "PaymentStatus không được để trống")]
    [JsonPropertyName("paymentStatus")]
    public string PaymentStatus { get; set; } = "UNPAID";

    /// <summary>
    /// Mức độ ưu tiên của phiên đặt sân trong ngữ cảnh thẩm định (NORMAL, HIGH, VIP, URGENT).
    /// </summary>
    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "NORMAL";

    /// <summary>
    /// Thời điểm khởi tạo yêu cầu từ client (UTC).
    /// </summary>
    [Required(ErrorMessage = "RequestedAt không được để trống")]
    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// Thuộc tính mở rộng bổ sung dưới dạng key-value (tùy chọn).
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
