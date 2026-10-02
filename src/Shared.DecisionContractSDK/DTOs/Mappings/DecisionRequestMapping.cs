using System.Text.RegularExpressions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace Shared.DecisionContractSDK.DTOs.Mappings;

/// <summary>
/// Ánh xạ request wire v2 sang domain theo hai tầng lỗi (B-06): mọi ràng buộc mà <c>decision-request.schema.json</c> diễn đạt
/// được (múi giờ, items, giá, currency, khách, lịch sử, nhu cầu, metadata) bị chặn trước khi dựng domain bằng
/// <see cref="ContractValidationException"/> (wire <c>VALIDATION_ERROR</c>, <c>ErrorCode</c> chi tiết, <c>FieldName</c> là
/// đường dẫn JSON); quy tắc ngoài schema (<c>endTime &gt; startTime</c>, định danh <see cref="Guid.Empty"/>) và các bất biến
/// còn lại vẫn do constructor domain ném <c>DomainInvariantException</c> (lớp phòng thủ).
/// </summary>
public static class DecisionRequestMapping
{
    // Cùng mẫu với schema (^[A-Z]{3}$) nhưng dùng \z để không nhận chuỗi có xuống dòng ở cuối.
    private static readonly Regex CurrencyPattern = new(@"^[A-Z]{3}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Dựng <see cref="Reservation"/> từ request v2.</summary>
    /// <param name="request">Request đã deserialize.</param>
    /// <exception cref="ContractValidationException">Khi request vi phạm ràng buộc hình thức của schema: múi giờ không phải mã IANA, items null/rỗng/quá 20/có phần tử null, giá hoặc tổng tiền âm, currency sai mẫu, thiếu hoặc mâu thuẫn thông tin khách, số đếm lịch sử âm, nhu cầu ngoài miền, metadata có giá trị null.</exception>
    public static Reservation ToDomain(this DecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeZone = ResolveIanaTimeZone(request.BranchTimeZone);
        ValidateItems(request.Items);
        ValidateAmounts(request);
        ValidateCustomer(request.Customer);
        ValidateDemand(request.Demand);

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

    private static void ValidateItems(IReadOnlyList<ReservationItemDto>? items)
    {
        if (items is null || items.Count is 0 or > Reservation.MaxItems)
        {
            throw new ContractValidationException("INVALID_ITEMS", $"items must contain 1..{Reservation.MaxItems} elements.", "items");
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null)
            {
                throw new ContractValidationException("INVALID_ITEMS", "items must not contain null elements.", $"items[{i}]");
            }

            if (item.Price < 0)
            {
                throw new ContractValidationException("INVALID_PRICE", "price must be greater than or equal to 0.", $"items[{i}].price");
            }
        }
    }

    private static void ValidateAmounts(DecisionRequestDto request)
    {
        if (request.TotalPrice < 0)
        {
            throw new ContractValidationException("INVALID_PRICE", "totalPrice must be greater than or equal to 0.", "totalPrice");
        }

        if (request.Currency is null || !CurrencyPattern.IsMatch(request.Currency))
        {
            throw new ContractValidationException(
                "INVALID_CURRENCY",
                $"currency '{request.Currency}' must be an ISO 4217 code of 3 upper-case letters.",
                "currency");
        }
    }

    private static void ValidateCustomer(CustomerDto? customer)
    {
        if (customer is null)
        {
            throw new ContractValidationException("INVALID_CUSTOMER", "customer must not be null.", "customer");
        }

        if (customer.IsGuest)
        {
            if (customer.CustomerId is not null)
            {
                throw new ContractValidationException("INVALID_CUSTOMER", "customerId must be null for a guest customer.", "customer.customerId");
            }

            if (customer.MemberSince is not null)
            {
                throw new ContractValidationException("INVALID_CUSTOMER", "memberSince must be null for a guest customer.", "customer.memberSince");
            }
        }
        else if (customer.CustomerId is null)
        {
            throw new ContractValidationException("INVALID_CUSTOMER", "customerId is required for a registered customer.", "customer.customerId");
        }

        if (customer.History is { } history)
        {
            ValidateCount(history.CompletedCount, "completedCount");
            ValidateCount(history.NoShowCount, "noShowCount");
            ValidateCount(history.CancelledCount, "cancelledCount");
            ValidateCount(history.LateCancelledCount, "lateCancelledCount");
            ValidateCount(history.PaymentExpiredCount, "paymentExpiredCount");
        }
    }

    private static void ValidateCount(int value, string property)
    {
        if (value < 0)
        {
            throw new ContractValidationException("INVALID_HISTORY", $"{property} must be greater than or equal to 0.", $"customer.history.{property}");
        }
    }

    private static void ValidateDemand(DemandDto? demand)
    {
        if (demand is null)
        {
            return;
        }

        if (demand.OccupancyRate is < 0m or > 1m)
        {
            throw new ContractValidationException("INVALID_DEMAND", "occupancyRate must be between 0 and 1.", "demand.occupancyRate");
        }

        if (demand.WaitlistCount < 0)
        {
            throw new ContractValidationException("INVALID_DEMAND", "waitlistCount must be greater than or equal to 0.", "demand.waitlistCount");
        }
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
