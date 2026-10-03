using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Outcomes;

/// <summary>Response 200 của outcome (INTEGRATION_SPEC §3.7); cũng là phần <c>outcome</c> của chi tiết quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OutcomeResponseDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Kết quả thực tế.</summary>
    public required DecisionOutcome Outcome { get; init; }

    /// <summary>Thời điểm xảy ra.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Thời điểm TADSS ghi nhận.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset ReportedAt { get; init; }
}
