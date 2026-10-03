using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Outcomes;

/// <summary>Request <c>POST /api/v2/decisions/{decisionId}/outcome</c> (INTEGRATION_SPEC §3.7).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OutcomeRequestDto
{
    /// <summary>Kết quả thực tế.</summary>
    public required DecisionOutcome Outcome { get; init; }

    /// <summary>Thời điểm xảy ra (có offset).</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OccurredAt { get; init; }
}
