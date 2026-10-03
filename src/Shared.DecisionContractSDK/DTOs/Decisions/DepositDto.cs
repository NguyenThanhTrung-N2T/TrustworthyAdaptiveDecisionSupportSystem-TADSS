using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Decisions;

/// <summary>Mức cọc đã quy đổi: <c>amount = round(totalPrice × rate)</c> theo số chữ số thập phân của tiền tệ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DepositDto
{
    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>.</summary>
    public required decimal Rate { get; init; }

    /// <summary>Số tiền cọc.</summary>
    public required decimal Amount { get; init; }

    /// <summary>Mã tiền tệ ISO 4217.</summary>
    public required string Currency { get; init; }
}
