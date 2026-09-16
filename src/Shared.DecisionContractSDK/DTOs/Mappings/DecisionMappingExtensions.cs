using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.DTOs.Mappings;

/// <summary>
/// Cung cấp các phương thức mở rộng chuyển đổi 2 chiều (Bidirectional Mapping) giữa Wire DTOs và Canonical Domain Models.
/// Áp dụng cơ chế kiểm tra nghiêm ngặt (Strict Canonical SCREAMING_SNAKE_CASE - Option A) cho toàn bộ Enums trên Wire Contract.
/// </summary>
public static class DecisionMappingExtensions
{
    /// <summary>
    /// Chuyển đổi từ DecisionRequestDto (Contract DTO) sang Reservation (Domain Aggregate Root).
    /// </summary>
    /// <param name="request">DTO nhận từ Wire JSON payload.</param>
    /// <returns>Đối tượng Reservation hợp lệ trong Domain.</returns>
    /// <exception cref="ArgumentNullException">Ném ra khi request null.</exception>
    /// <exception cref="ContractValidationException">Ném ra khi dữ liệu enum trên wire không đúng định dạng canonical SCREAMING_SNAKE_CASE hoặc không hợp lệ.</exception>
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
    /// Parse chuỗi PaymentStatus nghiêm ngặt theo định dạng Canonical SCREAMING_SNAKE_CASE:
    /// Rỗng/Null -> UNPAID, Chuỗi khác định dạng chuẩn hoặc không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    /// <param name="value">Chuỗi enum đại diện PaymentStatus.</param>
    /// <returns>Enum PaymentStatus tương ứng.</returns>
    /// <exception cref="ContractValidationException">Ném ra khi giá trị không đúng chuẩn SCREAMING_SNAKE_CASE hoặc không tồn tại.</exception>
    public static PaymentStatus ParsePaymentStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PaymentStatus.UNPAID;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<PaymentStatus>(trimmed, ignoreCase: false, out var parsedStatus)
            && Enum.IsDefined(parsedStatus)
            && trimmed == parsedStatus.ToString())
        {
            return parsedStatus;
        }

        throw new ContractValidationException(
            "INVALID_PAYMENT_STATUS",
            $"Giá trị PaymentStatus '{value}' không hợp lệ. Phải tuân theo định dạng chuẩn SCREAMING_SNAKE_CASE: UNPAID, PARTIALLY_PAID, PAID, REFUNDED, EXPIRED.",
            nameof(DecisionRequestDto.PaymentStatus));
    }

    /// <summary>
    /// Parse chuỗi BookingPriority nghiêm ngặt theo định dạng Canonical SCREAMING_SNAKE_CASE:
    /// Rỗng/Null -> NORMAL, Chuỗi khác định dạng chuẩn hoặc không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    /// <param name="value">Chuỗi enum đại diện BookingPriority.</param>
    /// <returns>Enum BookingPriority tương ứng.</returns>
    /// <exception cref="ContractValidationException">Ném ra khi giá trị không đúng chuẩn SCREAMING_SNAKE_CASE hoặc không tồn tại.</exception>
    public static BookingPriority ParseBookingPriority(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BookingPriority.NORMAL;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<BookingPriority>(trimmed, ignoreCase: false, out var parsedPriority)
            && Enum.IsDefined(parsedPriority)
            && trimmed == parsedPriority.ToString())
        {
            return parsedPriority;
        }

        throw new ContractValidationException(
            "INVALID_BOOKING_PRIORITY",
            $"Giá trị BookingPriority '{value}' không hợp lệ. Phải tuân theo định dạng chuẩn SCREAMING_SNAKE_CASE: NORMAL, HIGH, VIP, URGENT.",
            nameof(DecisionRequestDto.Priority));
    }

    /// <summary>
    /// Parse chuỗi DecisionStatus nghiêm ngặt theo định dạng Canonical SCREAMING_SNAKE_CASE:
    /// Rỗng/Null -> NEED_APPROVAL, Chuỗi khác định dạng chuẩn hoặc không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    /// <param name="value">Chuỗi enum đại diện DecisionStatus.</param>
    /// <returns>Enum DecisionStatus tương ứng.</returns>
    /// <exception cref="ContractValidationException">Ném ra khi giá trị không đúng chuẩn SCREAMING_SNAKE_CASE hoặc không tồn tại.</exception>
    public static DecisionStatus ParseDecisionStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DecisionStatus.NEED_APPROVAL;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<DecisionStatus>(trimmed, ignoreCase: false, out var parsedStatus)
            && Enum.IsDefined(parsedStatus)
            && trimmed == parsedStatus.ToString())
        {
            return parsedStatus;
        }

        throw new ContractValidationException(
            "INVALID_DECISION_STATUS",
            $"Giá trị DecisionStatus '{value}' không hợp lệ. Phải tuân theo định dạng chuẩn SCREAMING_SNAKE_CASE: APPROVED, REJECTED, NEED_APPROVAL.",
            nameof(DecisionResultDto.Status));
    }

    /// <summary>
    /// Parse chuỗi RecommendationType nghiêm ngặt theo định dạng Canonical SCREAMING_SNAKE_CASE:
    /// Chuỗi rỗng, không đúng định dạng chuẩn hoặc không hợp lệ -> Ném ContractValidationException.
    /// </summary>
    /// <param name="value">Chuỗi enum đại diện RecommendationType.</param>
    /// <returns>Enum RecommendationType tương ứng.</returns>
    /// <exception cref="ContractValidationException">Ném ra khi giá trị để trống, không đúng chuẩn SCREAMING_SNAKE_CASE hoặc không tồn tại.</exception>
    public static RecommendationType ParseRecommendationType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ContractValidationException(
                "INVALID_RECOMMENDATION_TYPE",
                "Giá trị RecommendationType không được để trống.",
                nameof(DecisionRecommendationDto.Type));
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<RecommendationType>(trimmed, ignoreCase: false, out var parsedType)
            && Enum.IsDefined(parsedType)
            && trimmed == parsedType.ToString())
        {
            return parsedType;
        }

        throw new ContractValidationException(
            "INVALID_RECOMMENDATION_TYPE",
            $"Giá trị RecommendationType '{value}' không hợp lệ. Phải tuân theo định dạng chuẩn SCREAMING_SNAKE_CASE: ALTERNATIVE_COURT, ALTERNATIVE_SLOT, REQUIRE_DEPOSIT, PRICE_ADJUSTMENT, REQUIRE_APPROVAL, PRIORITY_ESCALATION.",
            nameof(DecisionRecommendationDto.Type));
    }
}
