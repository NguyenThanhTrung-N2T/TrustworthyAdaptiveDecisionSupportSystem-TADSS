using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>Response 200 của override (INTEGRATION_SPEC §3.6); cũng là phần <c>override</c> của chi tiết quyết định.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideResponseDto
{
    /// <summary>Định danh quyết định.</summary>
    public required Guid DecisionId { get; init; }

    /// <summary>Hành động của Manager.</summary>
    public required OverrideAction Action { get; init; }

    /// <summary>Mức cọc đã quy đổi (rate và amount) khi duyệt; <c>null</c> khi từ chối.</summary>
    public required DepositDto? Deposit { get; init; }

    /// <summary>Định danh Manager (<c>X-Actor-Id</c>).</summary>
    public required string ActorId { get; init; }

    /// <summary>Lý do.</summary>
    public required string Reason { get; init; }

    /// <summary>Thời điểm override.</summary>
    [JsonConverter(typeof(OffsetDateTimeConverter))]
    public required DateTimeOffset OverriddenAt { get; init; }
}
