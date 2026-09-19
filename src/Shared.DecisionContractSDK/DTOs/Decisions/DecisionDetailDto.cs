using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Dữ liệu kiểm toán và giải trình chi tiết một quyết định đã lưu trong TADSS Audit Ledger.
/// </summary>
public class DecisionDetailDto
{
    /// <summary>
    /// Định danh duy nhất của quyết định thẩm định.
    /// </summary>
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    /// <summary>
    /// Định danh đơn đặt sân tương ứng.
    /// </summary>
    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Mã băm SHA-256 canonical của payload yêu cầu thẩm định.
    /// </summary>
    [JsonPropertyName("requestHash")]
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>
    /// Tóm tắt kết quả quyết định nghiệp vụ (Snapshot máy).
    /// </summary>
    [JsonPropertyName("decision")]
    public DecisionSummaryDto Decision { get; set; } = new();

    /// <summary>
    /// Tóm tắt thông tin thực thi kỹ thuật và hiệu năng.
    /// </summary>
    [JsonPropertyName("execution")]
    public ExecutionSummaryDto Execution { get; set; } = new();

    /// <summary>
    /// Trạng thái quản trị và can thiệp ghi đè (Human-in-the-loop).
    /// </summary>
    [JsonPropertyName("governance")]
    public GovernanceSummaryDto Governance { get; set; } = new();

    /// <summary>
    /// Dấu vết thời gian tạo và cập nhật bản ghi.
    /// </summary>
    [JsonPropertyName("timestamps")]
    public TimestampSummaryDto Timestamps { get; set; } = new();
}

/// <summary>
/// Tóm tắt kết quả nghiệp vụ của quyết định.
/// </summary>
public class DecisionSummaryDto
{
    /// <summary>
    /// Trạng thái quyết định (APPROVED, REJECTED, NEED_APPROVAL).
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Mã lý do chính giải thích cho quyết định.
    /// </summary>
    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    /// <summary>
    /// Danh sách tất cả các lý do chi tiết.
    /// </summary>
    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = [];

    /// <summary>
    /// Danh sách các khuyến nghị hoặc gợi ý thay thế (nếu có).
    /// </summary>
    [JsonPropertyName("recommendations")]
    public List<DecisionRecommendationDto> Recommendations { get; set; } = [];
}

/// <summary>
/// Tóm tắt thông tin thực thi kỹ thuật của Engine.
/// </summary>
public class ExecutionSummaryDto
{
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
    /// Hình thức thực thi kỹ thuật của Engine (AUTOMATED, FALLBACK, CACHE_HIT).
    /// </summary>
    [JsonPropertyName("executionType")]
    public string ExecutionType { get; set; } = "AUTOMATED";

    /// <summary>
    /// Thời gian thực thi thuật toán tính bằng mili-giây (ms).
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    /// <summary>
    /// Cờ cho biết có phải dùng kết quả Fallback an toàn hay không.
    /// </summary>
    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    /// <summary>
    /// Lý do gây ra fallback hoặc lỗi kỹ thuật (nếu có).
    /// </summary>
    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }

    /// <summary>
    /// Cờ cho biết kết quả có được lấy từ Cache 2 tầng (L1/L2) hay không.
    /// </summary>
    [JsonPropertyName("cacheHit")]
    public bool CacheHit { get; set; }
}

/// <summary>
/// Tóm tắt trạng thái quản trị và can thiệp của Quản lý.
/// </summary>
public class GovernanceSummaryDto
{
    /// <summary>
    /// Cờ cho biết ca này có đang chờ Quản lý duyệt hay không.
    /// </summary>
    [JsonPropertyName("needApproval")]
    public bool NeedApproval { get; set; }

    /// <summary>
    /// Cờ cho biết quyết định đã bị Quản lý can thiệp ghi đè hay chưa.
    /// </summary>
    [JsonPropertyName("isOverridden")]
    public bool IsOverridden { get; set; }

    /// <summary>
    /// Định danh hoặc tên người quản lý đã thực hiện ghi đè.
    /// </summary>
    [JsonPropertyName("overriddenBy")]
    public string? OverriddenBy { get; set; }

    /// <summary>
    /// Lý do giải trình cho hành động can thiệp ghi đè.
    /// </summary>
    [JsonPropertyName("overrideReason")]
    public string? OverrideReason { get; set; }

    /// <summary>
    /// Thời điểm thực hiện ghi đè (UTC).
    /// </summary>
    [JsonPropertyName("overriddenAt")]
    public DateTime? OverriddenAt { get; set; }
}

/// <summary>
/// Dấu vết mốc thời gian của bản ghi quyết định.
/// </summary>
public class TimestampSummaryDto
{
    /// <summary>
    /// Thời điểm khởi tạo bản ghi thẩm định (UTC).
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Thời điểm cập nhật trạng thái gần nhất (UTC).
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
