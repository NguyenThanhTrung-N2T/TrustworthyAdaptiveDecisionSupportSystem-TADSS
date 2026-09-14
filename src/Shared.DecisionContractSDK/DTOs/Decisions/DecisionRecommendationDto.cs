using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Chi tiết một đề xuất thay thế hoặc khuyến nghị tối ưu khi đơn chưa đủ điều kiện duyệt.
/// </summary>
public class DecisionRecommendationDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("suggestedCourtId")]
    public Guid? SuggestedCourtId { get; set; }

    [JsonPropertyName("suggestedStartTime")]
    public DateTime? SuggestedStartTime { get; set; }

    [JsonPropertyName("suggestedEndTime")]
    public DateTime? SuggestedEndTime { get; set; }

    [JsonPropertyName("suggestedPrice")]
    public decimal? SuggestedPrice { get; set; }

    [JsonPropertyName("confidenceScore")]
    public double? ConfidenceScore { get; set; }
}
