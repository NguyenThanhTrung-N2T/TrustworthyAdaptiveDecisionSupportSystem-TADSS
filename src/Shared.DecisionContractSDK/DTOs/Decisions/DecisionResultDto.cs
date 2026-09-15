using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Kết quả thẩm định quyết định tự động trả về từ TADSS Engine.
/// </summary>
public class DecisionResultDto
{
    /// <summary>
    /// Định danh duy nhất của quyết định vừa được thẩm định.
    /// </summary>
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    /// <summary>
    /// Định danh đơn đặt sân tương ứng.
    /// </summary>
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Trạng thái kết quả quyết định (APPROVED, REJECTED, NEED_APPROVAL).
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Tên chiến lược ra quyết định đã áp dụng.
    /// </summary>
    [JsonPropertyName("strategyName")]
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// Phiên bản của chiến lược ra quyết định.
    /// </summary>
    [JsonPropertyName("strategyVersion")]
    public string StrategyVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Thời gian thực thi thuật toán thẩm định (ms).
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    /// <summary>
    /// Cờ cho biết có phải dùng cơ chế Fallback an toàn hay không.
    /// </summary>
    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    /// <summary>
    /// Lý do chính giải thích cho quyết định (Machine-readable reason code).
    /// </summary>
    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách các lý do chi tiết giải thích cho quyết định.
    /// </summary>
    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = [];

    /// <summary>
    /// Danh sách các đề xuất thay thế hoặc khuyến nghị tối ưu (nếu có).
    /// </summary>
    [JsonPropertyName("recommendations")]
    public List<DecisionRecommendationDto> Recommendations { get; set; } = [];

    /// <summary>
    /// Thời điểm hoàn tất thẩm định quyết định (UTC).
    /// </summary>
    [JsonPropertyName("evaluatedAt")]
    public DateTime EvaluatedAt { get; set; }
}
