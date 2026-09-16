using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Phần tử đại diện cho một ca đặt sân đang chờ Quản lý duyệt trong hàng đợi.
/// </summary>
public class PendingApprovalItemDto
{
    /// <summary>
    /// Định danh duy nhất của quyết định.
    /// </summary>
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    /// <summary>
    /// Định danh đơn đặt sân tương ứng.
    /// </summary>
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Trạng thái hiện tại (mặc định NEED_APPROVAL).
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "NEED_APPROVAL";

    /// <summary>
    /// Mã lý do chính khiến đơn rơi vào trạng thái chờ duyệt.
    /// </summary>
    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    /// <summary>
    /// Tên chiến lược đã đánh giá đơn.
    /// </summary>
    [JsonPropertyName("strategyName")]
    public string StrategyName { get; set; } = string.Empty;

    /// <summary>
    /// Phiên bản của chiến lược đã đánh giá đơn.
    /// </summary>
    [JsonPropertyName("strategyVersion")]
    public string StrategyVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Thời gian thực thi thuật toán thẩm định (ms).
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    /// <summary>
    /// Cờ cho biết có phải dùng kết quả Fallback hay không.
    /// </summary>
    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    /// <summary>
    /// Thời điểm khởi tạo bản ghi thẩm định (UTC).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}
