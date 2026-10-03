using System.Text.Json.Serialization;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Governance;

/// <summary>Thông tin khách rút gọn trong hàng chờ.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PendingApprovalCustomerDto
{
    /// <summary>Khách vãng lai.</summary>
    public required bool IsGuest { get; init; }

    /// <summary>Hạng thành viên.</summary>
    public required CustomerTier Tier { get; init; }
}
