using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Enums;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Aggregate Root của một yêu cầu đánh giá v2: một hoặc nhiều item, khách, giá, tiền tệ, kênh, nhu cầu và múi giờ
/// chi nhánh. Bất biến phổ quát được kiểm tra trong constructor (ADR-004); chính sách nghiệp vụ (thời lượng tối
/// thiểu/tối đa) thuộc runtime (P-01).
/// </summary>
public sealed class Reservation
{
    /// <summary>Số item tối đa của một reservation.</summary>
    public const int MaxItems = 20;

    private static readonly Regex CurrencyPattern = new(@"^[A-Z]{3}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Định danh đơn phía client (SmashCourt: <c>bookingId</c>).</summary>
    public Guid ReservationId { get; }

    /// <summary>Định danh chi nhánh.</summary>
    public Guid BranchId { get; }

    /// <summary>Múi giờ của chi nhánh; mọi quy tắc theo khung giờ tính theo múi giờ này.</summary>
    public TimeZoneInfo BranchTimeZone { get; }

    /// <summary>Các item (1..<see cref="MaxItems"/>), theo thứ tự client gửi.</summary>
    public IReadOnlyList<ReservationItem> Items { get; }

    /// <summary>Hồ sơ khách.</summary>
    public CustomerProfile Customer { get; }

    /// <summary>Tổng tiền sau giảm giá, không âm; mức cọc tính trên số này.</summary>
    public decimal TotalPrice { get; }

    /// <summary>Mã tiền tệ ISO 4217 (3 chữ cái in hoa).</summary>
    public string Currency { get; }

    /// <summary>Kênh tạo booking.</summary>
    public Channel Channel { get; }

    /// <summary>Thời điểm client gửi yêu cầu.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Ảnh chụp nhu cầu tại khung giờ (tùy chọn).</summary>
    public DemandSnapshot? Demand { get; }

    /// <summary>Metadata không ảnh hưởng quyết định.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Thời điểm bắt đầu sớm nhất trong các item.</summary>
    public DateTimeOffset EarliestStart { get; }

    /// <summary>Khởi tạo reservation và kiểm tra bất biến.</summary>
    /// <param name="reservationId">Định danh đơn, khác rỗng.</param>
    /// <param name="branchId">Định danh chi nhánh, khác rỗng.</param>
    /// <param name="branchTimeZone">Múi giờ chi nhánh.</param>
    /// <param name="items">1..<see cref="MaxItems"/> item, không có phần tử null.</param>
    /// <param name="customer">Hồ sơ khách.</param>
    /// <param name="totalPrice">Tổng tiền, không âm.</param>
    /// <param name="currency">Mã ISO 4217 in hoa.</param>
    /// <param name="channel">Kênh tạo booking.</param>
    /// <param name="requestedAt">Thời điểm client gửi yêu cầu.</param>
    /// <param name="demand">Ảnh chụp nhu cầu (tùy chọn).</param>
    /// <param name="metadata">Metadata (tùy chọn).</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public Reservation(
        Guid reservationId,
        Guid branchId,
        TimeZoneInfo branchTimeZone,
        IReadOnlyList<ReservationItem> items,
        CustomerProfile customer,
        decimal totalPrice,
        string currency,
        Channel channel,
        DateTimeOffset requestedAt,
        DemandSnapshot? demand = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(branchTimeZone);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(customer);

        if (reservationId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESERVATION_ID", "ReservationId must not be empty.", nameof(ReservationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_BRANCH_ID", "BranchId must not be empty.", nameof(BranchId));
        }

        if (items.Count is 0 or > MaxItems || items.Any(item => item is null))
        {
            throw new DomainInvariantException("INVALID_ITEM_COUNT", $"Items must contain 1..{MaxItems} non-null elements.", nameof(Items));
        }

        if (totalPrice < 0)
        {
            throw new DomainInvariantException("INVALID_TOTAL_PRICE", "TotalPrice must be greater than or equal to 0.", nameof(TotalPrice));
        }

        if (currency is null || !CurrencyPattern.IsMatch(currency))
        {
            throw new DomainInvariantException("INVALID_CURRENCY", $"Currency '{currency}' must be an ISO 4217 code in upper case.", nameof(Currency));
        }

        if (!Enum.IsDefined(channel))
        {
            throw new DomainInvariantException("INVALID_CHANNEL", $"Channel '{channel}' is not defined.", nameof(Channel));
        }

        ReservationId = reservationId;
        BranchId = branchId;
        BranchTimeZone = branchTimeZone;
        Items = Array.AsReadOnly(items.ToArray());
        Customer = customer;
        TotalPrice = totalPrice;
        Currency = currency;
        Channel = channel;
        RequestedAt = requestedAt;
        Demand = demand;
        Metadata = new ReadOnlyDictionary<string, string>(metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        EarliestStart = Items.Min(item => item.Start);
    }

    /// <summary>Quy một thời điểm về giờ địa phương của chi nhánh.</summary>
    /// <param name="instant">Thời điểm cần quy đổi.</param>
    public DateTimeOffset ToBranchLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, BranchTimeZone);
}
