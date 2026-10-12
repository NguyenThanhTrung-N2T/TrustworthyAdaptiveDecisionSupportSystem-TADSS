using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>
/// Request <c>POST /api/v2/decisions/{decisionId}/override</c> (INTEGRATION_SPEC §3.6). <c>APPROVE</c> bắt buộc
/// đúng một trong <c>deposit.rate</c>/<c>deposit.amount</c>; <c>REJECT</c> không gửi <c>deposit</c> — runtime kiểm tra các ràng buộc này khi nhận request.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideRequestDto
{
    /// <summary>Hành động của Manager.</summary>
    public required OverrideAction Action { get; init; }

    /// <summary>Mức cọc khi <see cref="OverrideAction.APPROVE"/>.</summary>
    public OverrideDepositDto? Deposit { get; init; }

    /// <summary>Lý do (5..500 ký tự).</summary>
    public required string Reason { get; init; }
}
