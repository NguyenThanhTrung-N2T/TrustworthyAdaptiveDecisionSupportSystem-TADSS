using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Phần tử đại diện cho một ca đặt sân đang chờ Quản lý duyệt trong hàng đợi.
/// </summary>
public class PendingApprovalItemDto
{
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "NEED_APPROVAL";

    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    [JsonPropertyName("strategyName")]
    public string StrategyName { get; set; } = string.Empty;

    [JsonPropertyName("strategyVersion")]
    public string StrategyVersion { get; set; } = "1.0.0";

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
