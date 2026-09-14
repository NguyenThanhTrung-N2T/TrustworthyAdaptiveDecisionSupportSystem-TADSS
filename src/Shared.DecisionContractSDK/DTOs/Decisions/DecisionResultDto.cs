using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Kết quả thẩm định quyết định tự động trả về từ TADSS Engine.
/// </summary>
public class DecisionResultDto
{
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("strategyName")]
    public string StrategyName { get; set; } = string.Empty;

    [JsonPropertyName("strategyVersion")]
    public string StrategyVersion { get; set; } = "1.0.0";

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = [];

    [JsonPropertyName("recommendations")]
    public List<DecisionRecommendationDto> Recommendations { get; set; } = [];

    [JsonPropertyName("evaluatedAt")]
    public DateTime EvaluatedAt { get; set; }
}
