using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.Application.Abstractions.Persistence;

namespace TADSS.Application.Governance;

/// <summary>
/// So khớp "cùng payload" giữa request override và override đã lưu (F-03): cùng <c>action</c>, <c>reason</c> (so ordinal),
/// <c>actorId</c>, và cùng giá trị cọc mà request gửi — <c>rate</c> so với <c>deposit_rate</c>, <c>amount</c> so với <c>deposit_amount</c> đã lưu.
/// Thuần, không trạng thái.
/// </summary>
internal static class OverridePayloadComparer
{
    /// <summary>Request có cùng payload với override đã lưu hay không.</summary>
    public static bool IsSamePayload(DecisionOverrideRecord stored, OverrideRequestDto request, string actorId)
    {
        if (stored.Action != request.Action
            || !string.Equals(stored.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(stored.Reason, request.Reason, StringComparison.Ordinal))
        {
            return false;
        }

        // REJECT không có cọc (đã được validator bảo đảm); APPROVE so đúng giá trị mà request gửi
        return request.Deposit switch
        {
            { Rate: { } rate } => stored.DepositRate == rate,
            { Amount: { } amount } => stored.DepositAmount == amount,
            _ => stored.DepositRate is null && stored.DepositAmount is null,
        };
    }
}
