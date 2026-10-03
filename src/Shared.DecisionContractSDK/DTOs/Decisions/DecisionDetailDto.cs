using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Governance;
using Shared.DecisionContractSDK.DTOs.Outcomes;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

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
