namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Thông tin khách hàng đặt sân với hạng thành viên (Tier) động từ cơ sở dữ liệu LoyaltyTier.
/// </summary>
public record CustomerInfo
{
    public Guid CustomerId { get; init; }
    public string Tier { get; init; }

    public CustomerInfo(Guid customerId, string? tier = null)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_CUSTOMER_ID", "CustomerId không được để trống.", nameof(CustomerId));
        }

        CustomerId = customerId;
        Tier = string.IsNullOrWhiteSpace(tier) ? "STANDARD" : tier.Trim();
    }
}
