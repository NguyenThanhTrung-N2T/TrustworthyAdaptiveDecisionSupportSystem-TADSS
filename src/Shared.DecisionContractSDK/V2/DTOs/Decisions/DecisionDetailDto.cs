using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Governance;
using Shared.DecisionContractSDK.V2.DTOs.Outcomes;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.DTOs.Decisions;

/// <summary>
/// Response <c>GET /api/v2/decisions/{decisionId}</c> (INTEGRATION_SPEC §3.4).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionDetailDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Request v2 đã chuẩn hóa tại thời điểm đánh giá.</summary>
    public required DecisionRequestDto Request { get; init; }

    /// <summary>Kết quả máy.</summary>
    public required DecisionDetailResultDto Result { get; init; }

    /// <summary>Telemetry thực thi.</summary>
    public required ExecutionDetailDto Execution { get; init; }

    /// <summary>Override của Manager (tối đa một — INV-5), hoặc <c>null</c>.</summary>
    public required OverrideResponseDto? Override { get; init; }

    /// <summary>Kết quả thực tế, hoặc <c>null</c>.</summary>
    public required OutcomeResponseDto? Outcome { get; init; }

    /// <summary>Thời điểm ghi audit.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Correlation id của request đánh giá (nếu có).</summary>
    public required string? CorrelationId { get; init; }
}

/// <summary>Kết quả máy trong chi tiết quyết định, gồm trạng thái gốc của strategy.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionDetailResultDto
{
    /// <summary>Trạng thái trả cho client (sau approval window).</summary>
    public required DecisionStatus Status { get; init; }

    /// <summary>Trạng thái gốc do strategy/fallback trả.</summary>
    public required DecisionStatus StrategyStatus { get; init; }

    /// <summary>Mức cọc; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Mã lý do.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Lý do cho người đọc.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public required IReadOnlyList<DecisionRecommendationDto> Recommendations { get; init; }

    /// <summary>Hạn duyệt; chỉ có khi <see cref="DecisionStatus.NEED_APPROVAL"/>.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset? ApprovalExpiresAt { get; init; }
}

/// <summary>Telemetry thực thi của một quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExecutionDetailDto
{
    /// <summary>Tên strategy.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Phiên bản strategy.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Cách quyết định được tạo ra.</summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>Thời gian thực thi (ms).</summary>
    public required int ExecutionTimeMs { get; init; }

    /// <summary>Có dùng fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Lý do lỗi khi fallback (nếu có).</summary>
    public required string? FailureReason { get; init; }

    /// <summary>Lấy từ cache.</summary>
    public required bool CacheHit { get; init; }
}
