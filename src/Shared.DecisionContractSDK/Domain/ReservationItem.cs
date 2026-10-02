using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Một tài nguyên (sân, phòng…) được đặt trong một khoảng thời gian. Bất biến phổ quát duy nhất về thời gian
/// là <c>End &gt; Start</c>; giới hạn thời lượng là chính sách của runtime (P-01).
/// </summary>
public sealed class ReservationItem
{
    /// <summary>Định danh tài nguyên (SmashCourt: <c>courtId</c>).</summary>
    public Guid ResourceId { get; }

    /// <summary>Định danh loại tài nguyên (SmashCourt: <c>courtTypeId</c>).</summary>
    public Guid ResourceTypeId { get; }

    /// <summary>Thời điểm bắt đầu (có offset).</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Thời điểm kết thúc (có offset), luôn sau <see cref="Start"/>.</summary>
    public DateTimeOffset End { get; }

    /// <summary>Giá của item (SmashCourt: phí sân trước giảm giá), không âm.</summary>
    public decimal Price { get; }

    /// <summary>Thời lượng của item.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Khởi tạo item và kiểm tra bất biến.</summary>
    /// <param name="resourceId">Định danh tài nguyên, khác rỗng.</param>
    /// <param name="resourceTypeId">Định danh loại tài nguyên, khác rỗng.</param>
    /// <param name="start">Thời điểm bắt đầu.</param>
    /// <param name="end">Thời điểm kết thúc, sau <paramref name="start"/>.</param>
    /// <param name="price">Giá, không âm.</param>
    /// <exception cref="DomainInvariantException">Khi vi phạm bất biến.</exception>
    public ReservationItem(Guid resourceId, Guid resourceTypeId, DateTimeOffset start, DateTimeOffset end, decimal price)
    {
        if (resourceId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESOURCE_ID", "ResourceId must not be empty.", nameof(ResourceId));
        }

        if (resourceTypeId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESOURCE_TYPE_ID", "ResourceTypeId must not be empty.", nameof(ResourceTypeId));
        }

        if (end <= start)
        {
            throw new DomainInvariantException(
                "INVALID_ITEM_RANGE",
                $"End ({end:O}) must be greater than Start ({start:O}).",
                nameof(End));
        }

        if (price < 0)
        {
            throw new DomainInvariantException("INVALID_ITEM_PRICE", "Price must be greater than or equal to 0.", nameof(Price));
        }

        ResourceId = resourceId;
        ResourceTypeId = resourceTypeId;
        Start = start;
        End = end;
        Price = price;
    }
}
