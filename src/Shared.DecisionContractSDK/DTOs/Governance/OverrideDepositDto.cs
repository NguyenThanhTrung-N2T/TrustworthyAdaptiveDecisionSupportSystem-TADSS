using System.Text.Json.Serialization;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>Mức cọc Manager chọn: theo tỷ lệ hoặc theo số tiền.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OverrideDepositDto
{
    /// <summary>Tỷ lệ cọc trong <c>[0, 1]</c>.</summary>
    public decimal? Rate { get; init; }

    /// <summary>Số tiền cọc.</summary>
    public decimal? Amount { get; init; }
}
