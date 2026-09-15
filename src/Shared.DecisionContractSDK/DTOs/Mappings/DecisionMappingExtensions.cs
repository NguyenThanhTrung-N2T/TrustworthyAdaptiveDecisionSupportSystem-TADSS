using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Mappings;

/// <summary>
/// Cung cấp các phương thức mở rộng chuyển đổi 2 chiều (Bidirectional Mapping) giữa Wire DTOs và Canonical Domain Models.
/// </summary>
public static class DecisionMappingExtensions
{
    /// <summary>
    /// Chuyển đổi từ DecisionRequestDto (Contract DTO) sang Reservation (Domain Aggregate Root).
    /// </summary>
    /// <param name="request">DTO nhận từ Wire JSON payload.</param>
    /// <returns>Đối tượng Reservation hợp lệ trong Domain.</returns>
    /// <exception cref="ArgumentNullException">Ném ra khi request null.</exception>
    /// <exception cref="ContractValidationException">Ném ra khi dữ liệu enum trên wire không hợp lệ.</exception>
    public static Reservation ToDomain(this DecisionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var paymentStatus = ParsePaymentStatus(request.PaymentStatus);
        var priority = ParseBookingPriority(request.Priority);

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
            priority,
            request.RequestedAt,
            request.Metadata);
    }

    /// <summary>
    /// Chuyển đổi từ Reservation (Domain Aggregate Root) sang DecisionRequestDto (Contract DTO) phục vụ roundtrip serialization.
    /// </summary>
    /// <param name="reservation">Đối tượng Domain Reservation.</param>
    /// <returns>DTO tương ứng với wire format chuẩn.</returns>
    /// <exception cref="ArgumentNullException">Ném ra khi reservation null.</exception>
    public static DecisionRequestDto ToDto(this Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        return new DecisionRequestDto
        {
            ReservationId = reservation.ReservationId,
            CustomerId = reservation.Customer.CustomerId,
            CustomerTier = reservation.Customer.Tier,
            CourtId = reservation.Resource.CourtId,
            BranchId = reservation.Resource.BranchId,
            CourtTypeId = reservation.Resource.CourtTypeId,
            StartTime = reservation.Schedule.StartTime,
            EndTime = reservation.Schedule.EndTime,
            TotalPrice = reservation.Payment.TotalPrice,
            DepositAmount = reservation.Payment.DepositAmount,
            PaymentStatus = reservation.Payment.PaymentStatus.ToString(),
            BookingType = reservation.BookingType,
            Priority = reservation.Priority.ToString(),
            RequestedAt = reservation.RequestedAt,
            Metadata = reservation.Metadata.Attributes.Count > 0
                ? new Dictionary<string, string>(reservation.Metadata.Attributes)
                : null
        };
    }

    /// <summary>
    /// Parse chuỗi PaymentStatus an toàn có phân biệt: Rỗng/Null -> UNPAID, Chuỗi lạ không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    private static PaymentStatus ParsePaymentStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PaymentStatus.UNPAID;
        }

        var normalized = NormalizeEnumString(value);
        if (Enum.TryParse<PaymentStatus>(normalized, true, out var parsedStatus))
        {
            return parsedStatus;
        }

        throw new ContractValidationException(
            "INVALID_PAYMENT_STATUS",
            $"Giá trị PaymentStatus '{value}' không hợp lệ. Các giá trị hợp lệ: UNPAID, PARTIALLY_PAID, PAID, REFUNDED, EXPIRED.",
            nameof(DecisionRequestDto.PaymentStatus));
    }

    /// <summary>
    /// Parse chuỗi BookingPriority an toàn có phân biệt: Rỗng/Null -> NORMAL, Chuỗi lạ không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    private static BookingPriority ParseBookingPriority(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BookingPriority.NORMAL;
        }

        var normalized = NormalizeEnumString(value);
        if (Enum.TryParse<BookingPriority>(normalized, true, out var parsedPriority))
        {
            return parsedPriority;
        }

        throw new ContractValidationException(
            "INVALID_BOOKING_PRIORITY",
            $"Giá trị BookingPriority '{value}' không hợp lệ trong ngữ cảnh TADSS. Các giá trị hợp lệ: NORMAL, HIGH, VIP, URGENT.",
            nameof(DecisionRequestDto.Priority));
    }

    private static string NormalizeEnumString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withUnderscores = System.Text.RegularExpressions.Regex.Replace(
            value.Trim(),
            "([a-z0-9])([A-Z])",
            "$1_$2");

        return withUnderscores.Replace("-", "_").ToUpperInvariant();
    }
}
