using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Dữ liệu kiểm toán và giải trình chi tiết một quyết định đã lưu.
/// </summary>
public class DecisionDetailDto
{
    [JsonPropertyName("decisionId")]
    public Guid DecisionId { get; set; }

    [JsonPropertyName("reservationId")]
    public Guid ReservationId { get; set; }

    [JsonPropertyName("requestHash")]
    public string RequestHash { get; set; } = string.Empty;

    [JsonPropertyName("decision")]
    public DecisionSummaryDto Decision { get; set; } = new();

    [JsonPropertyName("execution")]
    public ExecutionSummaryDto Execution { get; set; } = new();

    [JsonPropertyName("governance")]
    public GovernanceSummaryDto Governance { get; set; } = new();

    [JsonPropertyName("timestamps")]
    public TimestampSummaryDto Timestamps { get; set; } = new();
}

public class DecisionSummaryDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("primaryReason")]
    public string PrimaryReason { get; set; } = string.Empty;

    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = [];

    [JsonPropertyName("recommendations")]
    public List<DecisionRecommendationDto> Recommendations { get; set; } = [];
}

public class ExecutionSummaryDto
{
    [JsonPropertyName("strategyName")]
    public string StrategyName { get; set; } = string.Empty;

    [JsonPropertyName("strategyVersion")]
    public string StrategyVersion { get; set; } = "1.0.0";

    [JsonPropertyName("executionType")]
    public string ExecutionType { get; set; } = "AUTOMATED";

    [JsonPropertyName("executionTimeMs")]
    public int ExecutionTimeMs { get; set; }

    [JsonPropertyName("fallbackUsed")]
    public bool FallbackUsed { get; set; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("cacheHit")]
    public bool CacheHit { get; set; }
}

public class GovernanceSummaryDto
{
    [JsonPropertyName("needApproval")]
    public bool NeedApproval { get; set; }

    [JsonPropertyName("isOverridden")]
    public bool IsOverridden { get; set; }

    [JsonPropertyName("overriddenBy")]
    public string? OverriddenBy { get; set; }

    [JsonPropertyName("overrideReason")]
    public string? OverrideReason { get; set; }

    [JsonPropertyName("overriddenAt")]
    public DateTime? OverriddenAt { get; set; }
}

public class TimestampSummaryDto
{
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
