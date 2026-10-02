using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;

namespace Shared.DecisionContractSDK.V2.DTOs.Mappings;

/// <summary>
/// Ánh xạ request wire v2 sang domain. Lỗi định dạng (múi giờ không phải IANA, item null) →
/// <see cref="ContractValidationException"/>; vi phạm bất biến → <c>DomainInvariantException</c> từ constructor domain.
/// </summary>
public static class DecisionRequestMapping
{
    /// <summary>Dựng <see cref="Reservation"/> từ request v2.</summary>
    /// <param name="request">Request đã deserialize.</param>
    /// <exception cref="ContractValidationException">Khi múi giờ không phải mã IANA, danh sách item có phần tử null, thiếu thông tin khách hoặc metadata có giá trị null.</exception>
    public static Reservation ToDomain(this DecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeZone = ResolveIanaTimeZone(request.BranchTimeZone);

        if (request.Items is null || request.Items.Any(item => item is null))
        {
            throw new ContractValidationException("INVALID_ITEMS", "items must be a non-null array without null elements.", "items");
        }

        if (request.Customer is null)
        {
            throw new ContractValidationException("INVALID_CUSTOMER", "customer must not be null.", "customer");
        }

        if (request.Metadata is not null && request.Metadata.Any(entry => entry.Value is null))
        {
            throw new ContractValidationException("INVALID_METADATA", "metadata values must not be null.", "metadata");
        }

        var items = request.Items
            .Select(item => new ReservationItem(item.ResourceId, item.ResourceTypeId, item.StartTime, item.EndTime, item.Price))
            .ToArray();

        var history = request.Customer.History is { } h
            ? new CustomerHistory(h.CompletedCount, h.NoShowCount, h.CancelledCount, h.LateCancelledCount, h.PaymentExpiredCount, h.LastCompletedAt)
            : null;

        var customer = new CustomerProfile(
            request.Customer.IsGuest,
            request.Customer.CustomerId,
            request.Customer.Tier,
            request.Customer.MemberSince,
            history);

        var demand = request.Demand is { } d ? new DemandSnapshot(d.OccupancyRate, d.WaitlistCount) : null;

        return new Reservation(
            request.ReservationId,
            request.BranchId,
            timeZone,
            items,
            customer,
            request.TotalPrice,
            request.Currency,
            request.Channel,
            request.RequestedAt,
            demand,
            request.Metadata);
    }

    private static TimeZoneInfo ResolveIanaTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            throw new ContractValidationException(
                "INVALID_TIME_ZONE",
                $"branchTimeZone '{timeZoneId}' is not a valid IANA time zone identifier.",
                "branchTimeZone");
        }

        return timeZone;
    }
}
