using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Hồ sơ khách tại thời điểm đánh giá. Khách vãng lai không có <see cref="CustomerId"/> và <see cref="MemberSince"/>;
/// khách thành viên bắt buộc có <see cref="CustomerId"/>.
/// </summary>
public sealed class CustomerProfile
{
    /// <summary>Khách vãng lai.</summary>
    public bool IsGuest { get; }

    /// <summary>Định danh khách; <c>null</c> khi <see cref="IsGuest"/>.</summary>
    public Guid? CustomerId { get; }

    /// <summary>Hạng thành viên chuẩn hóa.</summary>
    public CustomerTier Tier { get; }

    /// <summary>Thời điểm tạo tài khoản (thâm niên); <c>null</c> khi khách vãng lai hoặc client không gửi.</summary>
    public DateTimeOffset? MemberSince { get; }

    /// <summary>Lịch sử đặt chỗ (tùy chọn).</summary>
    public CustomerHistory? History { get; }

    /// <summary>Khởi tạo hồ sơ khách và kiểm tra bất biến.</summary>
    /// <param name="isGuest">Khách vãng lai.</param>
    /// <param name="customerId">Bắt buộc (khác rỗng) khi không phải khách vãng lai; <c>null</c> khi là khách vãng lai.</param>
    /// <param name="tier">Hạng thành viên.</param>
    /// <param name="memberSince">Thời điểm tạo tài khoản; phải <c>null</c> với khách vãng lai.</param>
    /// <param name="history">Lịch sử đặt chỗ (tùy chọn).</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public CustomerProfile(bool isGuest, Guid? customerId, CustomerTier tier, DateTimeOffset? memberSince, CustomerHistory? history)
    {
        if (isGuest && customerId is not null)
        {
            throw new DomainInvariantException("GUEST_WITH_CUSTOMER_ID", "CustomerId must be null for a guest customer.", nameof(CustomerId));
        }

        if (isGuest && memberSince is not null)
        {
            throw new DomainInvariantException("GUEST_WITH_MEMBER_SINCE", "MemberSince must be null for a guest customer.", nameof(MemberSince));
        }

        if (!isGuest && (customerId is null || customerId == Guid.Empty))
        {
            throw new DomainInvariantException("MISSING_CUSTOMER_ID", "CustomerId is required for a registered customer.", nameof(CustomerId));
        }

        if (!Enum.IsDefined(tier))
        {
            throw new DomainInvariantException("INVALID_CUSTOMER_TIER", $"Tier '{tier}' is not defined.", nameof(Tier));
        }

        IsGuest = isGuest;
        CustomerId = customerId;
        Tier = tier;
        MemberSince = memberSince;
        History = history;
    }
}
