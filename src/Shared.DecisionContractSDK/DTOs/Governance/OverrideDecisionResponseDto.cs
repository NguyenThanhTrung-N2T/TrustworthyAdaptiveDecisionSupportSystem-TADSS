using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Phản hồi xác nhận kết quả ghi đè thành công từ Quản lý.
/// </summary>
public class OverrideDecisionResponseDto
{
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [JsonPropertyName("originalStatus")]
    public string OriginalStatus { get; set; } = "NEED_APPROVAL";

    [JsonPropertyName("overriddenStatus")]
    public string OverriddenStatus { get; set; } = "APPROVED";

    [JsonPropertyName("isOverridden")]
    public bool IsOverridden { get; set; } = true;

    [JsonPropertyName("overriddenBy")]
    public string OverriddenBy { get; set; } = string.Empty;

    [JsonPropertyName("overrideReason")]
    public string OverrideReason { get; set; } = string.Empty;

    [JsonPropertyName("overriddenAt")]
    public DateTime OverriddenAt { get; set; }
}
