using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

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
