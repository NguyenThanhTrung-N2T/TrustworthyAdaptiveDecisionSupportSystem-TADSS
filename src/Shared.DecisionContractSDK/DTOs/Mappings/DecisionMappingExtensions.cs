using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Mappings;

public static class DecisionMappingExtensions
{
    public static Reservation ToDomain(this DecisionRequestDto request)
    {
        var paymentStatus = Enum.TryParse<PaymentStatus>(NormalizeEnumString(request.PaymentStatus), true, out var parsedPayment)
            ? parsedPayment
            : PaymentStatus.UNPAID;

        return Reservation.Create(
            request.ReservationId,
            request.CustomerId,
            request.CustomerTier,
            request.CourtId,
            request.BranchId,
            request.CourtTypeId,
            request.StartTime,
            request.EndTime,
            request.TotalPrice,
            request.DepositAmount,
            paymentStatus,
            request.BookingType,
            request.Priority,
            request.RequestedAt,
            request.Metadata);
    }

    private static string NormalizeEnumString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim().Replace("-", "_").ToUpperInvariant();
    }
}
