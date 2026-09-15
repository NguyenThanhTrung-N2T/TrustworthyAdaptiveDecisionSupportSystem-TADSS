namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Value Object đại diện cho thông tin khách hàng đặt sân với phân hạng thành viên (Tier) động từ hệ sinh thái SmashCourt.
/// </summary>
public record CustomerInfo
{
    /// <summary>
    /// Định danh duy nhất của khách hàng (Khóa chính).
    /// </summary>
    public Guid CustomerId { get; init; }

    /// <summary>
    /// Hạng thành viên của khách hàng (STANDARD, SILVER, GOLD, VIP, DIAMOND,...).
    /// </summary>
    public string Tier { get; init; }

    /// <summary>
    /// Khởi tạo CustomerInfo với kiểm tra định danh hợp lệ.
    /// </summary>
    /// <param name="customerId">Định danh khách hàng (không được rỗng).</param>
    /// <param name="tier">Hạng thành viên (mặc định là STANDARD nếu rỗng).</param>
    /// <exception cref="DomainInvariantException">Ném ra khi CustomerId rỗng.</exception>
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
