using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Aggregate Root biểu diễn một yêu cầu đặt sân trong ranh giới nghiệp vụ của TADSS Decision Engine.
/// </summary>
public class Reservation
{
    public Guid ReservationId { get; }
    public CustomerInfo Customer { get; }
    public ResourceInfo Resource { get; }
    public ReservationSchedule Schedule { get; }
    public PaymentPolicy Payment { get; }
    public string BookingType { get; }
    public string Priority { get; }
    public DateTime RequestedAt { get; }
    public ReservationMetadata Metadata { get; }

    public Reservation(
        Guid reservationId,
        CustomerInfo customer,
        ResourceInfo resource,
        ReservationSchedule schedule,
        PaymentPolicy payment,
        string? bookingType,
        string? priority,
        DateTime requestedAt,
        ReservationMetadata? metadata = null)
    {
        if (reservationId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESERVATION_ID", "ReservationId must not be empty.", nameof(ReservationId));
        }

        ReservationId = reservationId;
        Customer = customer ?? throw new ArgumentNullException(nameof(customer));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        Payment = payment ?? throw new ArgumentNullException(nameof(payment));
        BookingType = string.IsNullOrWhiteSpace(bookingType) ? "SINGLE" : bookingType.Trim();
        Priority = string.IsNullOrWhiteSpace(priority) ? "NORMAL" : priority.Trim();
        RequestedAt = requestedAt;
        Metadata = metadata ?? new ReservationMetadata();
    }

    public static Reservation Create(
        Guid reservationId,
        Guid customerId,
        string? customerTier,
        Guid courtId,
        Guid branchId,
        Guid? courtTypeId,
        DateTime startTime,
        DateTime endTime,
        decimal totalPrice,
        decimal depositAmount,
        PaymentStatus paymentStatus,
        string? bookingType,
        string? priority,
        DateTime requestedAt,
        IDictionary<string, string>? metadata = null)
    {
        var customer = new CustomerInfo(customerId, customerTier);
        var resource = new ResourceInfo(courtId, branchId, courtTypeId);
        var schedule = new ReservationSchedule(startTime, endTime);
        var payment = new PaymentPolicy(totalPrice, depositAmount, paymentStatus);

        return new Reservation(
            reservationId,
            customer,
            resource,
            schedule,
            payment,
            bookingType,
            priority,
            requestedAt,
            new ReservationMetadata(metadata));
    }
}
