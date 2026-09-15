using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Phản hồi xác nhận kết quả ghi đè thành công từ Quản lý.
/// </summary>
public class OverrideDecisionResponseDto
{
    /// <summary>
    /// Định danh duy nhất của quyết định vừa được ghi đè.
    /// </summary>
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    /// <summary>
    /// Định danh đơn đặt sân tương ứng.
    /// </summary>
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Trạng thái gốc ban đầu của máy trước khi bị can thiệp.
    /// </summary>
    [JsonPropertyName("originalStatus")]
    public string OriginalStatus { get; set; } = "NEED_APPROVAL";

    /// <summary>
    /// Trạng thái mới sau khi được Quản lý ghi đè.
    /// </summary>
    [JsonPropertyName("overriddenStatus")]
    public string OverriddenStatus { get; set; } = "APPROVED";

    /// <summary>
    /// Cờ xác nhận quyết định đã được ghi đè thành công (luôn là true).
    /// </summary>
    [JsonPropertyName("isOverridden")]
    public bool IsOverridden { get; set; } = true;

    /// <summary>
    /// Tên hoặc định danh của Quản lý đã thực hiện ghi đè.
    /// </summary>
    [JsonPropertyName("overriddenBy")]
    public string OverriddenBy { get; set; } = string.Empty;

    /// <summary>
    /// Lý do giải trình cho hành động can thiệp ghi đè.
    /// </summary>
    [JsonPropertyName("overrideReason")]
    public string OverrideReason { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm thực hiện ghi đè thành công (UTC).
    /// </summary>
    [JsonPropertyName("overriddenAt")]
    public DateTime OverriddenAt { get; set; }
}
