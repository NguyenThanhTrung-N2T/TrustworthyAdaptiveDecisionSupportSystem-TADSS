using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Chi tiết một đề xuất thay thế hoặc khuyến nghị tối ưu khi đơn chưa đủ điều kiện duyệt.
/// </summary>
public class DecisionRecommendationDto
{
    /// <summary>
    /// Loại đề xuất (ví dụ: ALTERNATIVE_SLOT, REQUIRE_DEPOSIT, APPLY_PROMOTION, ADJUST_DURATION).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Thông điệp giải thích khuyến nghị cho người dùng hoặc nhân viên.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Định danh sân gợi ý thay thế (nếu có).
    /// </summary>
    [JsonPropertyName("suggestedCourtId")]
    public Guid? SuggestedCourtId { get; set; }

    /// <summary>
    /// Khung giờ bắt đầu gợi ý thay thế (nếu có).
    /// </summary>
    [JsonPropertyName("suggestedStartTime")]
    public DateTime? SuggestedStartTime { get; set; }

    /// <summary>
    /// Khung giờ kết thúc gợi ý thay thế (nếu có).
    /// </summary>
    [JsonPropertyName("suggestedEndTime")]
    public DateTime? SuggestedEndTime { get; set; }

    /// <summary>
    /// Mức giá hoặc số tiền cọc đề xuất điều chỉnh (nếu có).
    /// </summary>
    [JsonPropertyName("suggestedPrice")]
    public decimal? SuggestedPrice { get; set; }

    /// <summary>
    /// Độ tin cậy của thuật toán gợi ý (từ 0.0 đến 1.0).
    /// </summary>
    [JsonPropertyName("confidenceScore")]
    public double? ConfidenceScore { get; set; }
}
