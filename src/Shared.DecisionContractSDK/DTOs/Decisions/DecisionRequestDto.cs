using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Yêu cầu thẩm định quyết định đặt sân gửi từ SmashCourt-BE sang TADSS Engine.
/// </summary>
public class DecisionRequestDto
{
    [Required(ErrorMessage = "ReservationId không được để trống")]
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [Required(ErrorMessage = "CourtId không được để trống")]
    [JsonPropertyName("courtId")]
    public Guid CourtId { get; set; }

    [JsonPropertyName("courtTypeId")]
    public Guid? CourtTypeId { get; set; }

    [Required(ErrorMessage = "BranchId không được để trống")]
    [JsonPropertyName("branchId")]
    public Guid BranchId { get; set; }

    [Required(ErrorMessage = "CustomerId không được để trống")]
    [JsonPropertyName("customerId")]
    public Guid CustomerId { get; set; }

    [JsonPropertyName("customerTier")]
    public string? CustomerTier { get; set; } = "STANDARD";

    [Required(ErrorMessage = "StartTime không được để trống")]
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [Required(ErrorMessage = "EndTime không được để trống")]
    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }

    [Required(ErrorMessage = "BookingType không được để trống")]
    [JsonPropertyName("bookingType")]
    public string BookingType { get; set; } = "SINGLE";

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "TotalPrice phải lớn hơn hoặc bằng 0")]
    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "DepositAmount phải lớn hơn hoặc bằng 0")]
    [JsonPropertyName("depositAmount")]
    public decimal DepositAmount { get; set; }

    [Required(ErrorMessage = "PaymentStatus không được để trống")]
    [JsonPropertyName("paymentStatus")]
    public string PaymentStatus { get; set; } = "UNPAID";

    [JsonPropertyName("priority")]
    public string? Priority { get; set; } = "NORMAL";

    [Required(ErrorMessage = "RequestedAt không được để trống")]
    [JsonPropertyName("requestedAt")]
    public DateTime RequestedAt { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}
