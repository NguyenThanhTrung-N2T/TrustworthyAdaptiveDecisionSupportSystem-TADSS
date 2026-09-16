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
    /// Khởi tạo CustomerInfo với kiểm tra định danh và độ dài phân hạng hợp lệ.
    /// </summary>
    /// <param name="customerId">Định danh khách hàng (không được rỗng).</param>
    /// <param name="tier">Hạng thành viên (mặc định là STANDARD nếu rỗng, tối đa 50 ký tự).</param>
    /// <exception cref="DomainInvariantException">Ném ra khi CustomerId rỗng hoặc Tier vượt quá 50 ký tự.</exception>
    public CustomerInfo(Guid customerId, string? tier = null)
    {
        if (customerId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_CUSTOMER_ID", "CustomerId không được để trống.", nameof(CustomerId));
        }

        var sanitizedTier = string.IsNullOrWhiteSpace(tier) ? "STANDARD" : tier.Trim();
        if (sanitizedTier.Length > 50)
        {
            throw new DomainInvariantException(
                "INVALID_CUSTOMER_TIER_LENGTH",
                "CustomerTier must not exceed 50 characters.",
                nameof(Tier));
        }

        CustomerId = customerId;
        Tier = sanitizedTier;
    }
}
