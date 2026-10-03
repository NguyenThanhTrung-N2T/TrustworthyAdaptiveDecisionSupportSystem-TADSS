using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>
/// Response 200 của <c>POST /api/v2/decisions</c> (INTEGRATION_SPEC §3.3).
/// <see cref="Deposit"/> có ⇔ <see cref="DecisionStatus.APPROVED"/>; <see cref="ApprovalExpiresAt"/> có ⇔
/// <see cref="DecisionStatus.NEED_APPROVAL"/> (INV-7). Hai trường này luôn xuất hiện trên wire, có thể là <c>null</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DecisionResultDto
{
    /// <summary>Định danh quyết định do runtime cấp.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Định danh đơn phía client.</summary>
    public required Guid ReservationId { get; init; }

    /// <summary>Trạng thái quyết định.</summary>
    public required DecisionStatus Status { get; init; }

    /// <summary>Mức cọc; chỉ có khi <see cref="DecisionStatus.APPROVED"/>.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Tên strategy đã chạy.</summary>
    public required string StrategyName { get; init; }

    /// <summary>Phiên bản strategy.</summary>
    public required string StrategyVersion { get; init; }

    /// <summary>Cách quyết định được tạo ra.</summary>
    public required ExecutionType ExecutionType { get; init; }

    /// <summary>Có dùng fallback.</summary>
    public required bool FallbackUsed { get; init; }

    /// <summary>Mã lý do.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>Lý do cho người đọc.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>Khuyến nghị kèm theo.</summary>
    public required IReadOnlyList<DecisionRecommendationDto> Recommendations { get; init; }

    /// <summary>Hạn duyệt; chỉ có khi <see cref="DecisionStatus.NEED_APPROVAL"/>.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset? ApprovalExpiresAt { get; init; }

    /// <summary>Thời điểm đánh giá.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset EvaluatedAt { get; init; }
}
