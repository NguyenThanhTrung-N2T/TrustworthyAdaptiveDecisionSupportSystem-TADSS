using Shared.DecisionContractSDK.Enums;

namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Aggregate Root đại diện cho một yêu cầu đặt sân trong ranh giới nghiệp vụ của TADSS Decision Engine.
/// Đảm bảo tính bất biến (Immutable) sau khi khởi tạo và tuân thủ chặt chẽ các quy tắc Domain Invariant.
/// </summary>
public class Reservation
{
    /// <summary>
    /// Định danh duy nhất của đơn đặt sân (Khóa chính phía ứng dụng).
    /// </summary>
    public Guid ReservationId { get; }

    /// <summary>
    /// Thông tin khách hàng thực hiện đặt sân.
    /// </summary>
    public CustomerInfo Customer { get; }

    /// <summary>
    /// Thông tin tài nguyên sân bãi và chi nhánh.
    /// </summary>
    public ResourceInfo Resource { get; }

    /// <summary>
    /// Khung thời gian đặt sân.
    /// </summary>
    public ReservationSchedule Schedule { get; }

    /// <summary>
    /// Chính sách và trạng thái thanh toán.
    /// </summary>
    public PaymentPolicy Payment { get; }

    /// <summary>
    /// Loại hình đặt sân (ví dụ: SINGLE, RECURRING, EVENT).
    /// </summary>
    public string BookingType { get; }

    /// <summary>
    /// Mức độ ưu tiên của phiên đặt sân trong ngữ cảnh thẩm định TADSS.
    /// LƯU Ý: Đây là Request Priority, không phải Customer Loyalty Tier.
    /// </summary>
    public BookingPriority Priority { get; }

    /// <summary>
    /// Thời điểm gửi yêu cầu thẩm định (UTC).
    /// </summary>
    public DateTime RequestedAt { get; }

    /// <summary>
    /// Metadata bổ sung dưới dạng key-value.
    /// </summary>
    public ReservationMetadata Metadata { get; }

    /// <summary>
    /// Khởi tạo một đối tượng Reservation với kiểm tra Fail-Fast Invariants toàn diện.
    /// </summary>
    public Reservation(
        Guid reservationId,
        CustomerInfo customer,
        ResourceInfo resource,
        ReservationSchedule schedule,
        PaymentPolicy payment,
        string? bookingType,
        BookingPriority priority,
        DateTime requestedAt,
        ReservationMetadata? metadata = null)
    {
        var sanitizedBookingType = string.IsNullOrWhiteSpace(bookingType) ? "SINGLE" : bookingType.Trim();
        if (sanitizedBookingType.Length > 50)
        {
            throw new DomainInvariantException(
                "INVALID_BOOKING_TYPE_LENGTH",
                "BookingType must not exceed 50 characters.",
                nameof(BookingType));
        }

        ReservationId = reservationId;
        Customer = customer ?? throw new ArgumentNullException(nameof(customer));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        Payment = payment ?? throw new ArgumentNullException(nameof(payment));
        BookingType = sanitizedBookingType;
        Priority = priority;
        RequestedAt = requestedAt;
        Metadata = metadata ?? new ReservationMetadata();

        // Fail-Fast: Đảm bảo thực thể luôn ở trạng thái hợp lệ ngay sau khi khởi tạo
        EnsureValid();
    }

    /// <summary>
    /// Factory method hỗ trợ khởi tạo nhanh Reservation từ các kiểu dữ liệu nguyên thủy.
    /// </summary>
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
        BookingPriority priority,
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

    /// <summary>
    /// Kiểm tra tính hợp lệ toàn diện của toàn bộ Aggregate Root và các Value Objects trực thuộc.
    /// </summary>
    /// <exception cref="DomainInvariantException">Ném ra khi phát hiện bất kỳ vi phạm Invariant nào.</exception>
    public void EnsureValid()
    {
        ValidateIdentity();
        ValidateSchedule();
        ValidatePayment();
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn về mặt định danh của Reservation, Customer và Resource.
    /// </summary>
    private void ValidateIdentity()
    {
        if (ReservationId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESERVATION_ID", "ReservationId must not be empty.", nameof(ReservationId));
        }

        if (Customer == null || Customer.CustomerId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_CUSTOMER_ID", "CustomerId must not be empty.", nameof(Customer));
        }

        if (Resource == null || Resource.CourtId == Guid.Empty || Resource.BranchId == Guid.Empty)
        {
            throw new DomainInvariantException("INVALID_RESOURCE_ID", "CourtId and BranchId must not be empty.", nameof(Resource));
        }
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn về khung thời gian đặt sân.
    /// </summary>
    private void ValidateSchedule()
    {
        if (Schedule == null)
        {
            throw new DomainInvariantException("INVALID_SCHEDULE", "Schedule must not be null.", nameof(Schedule));
        }

        if (Schedule.EndTime <= Schedule.StartTime)
        {
            throw new DomainInvariantException(
                "INVALID_SCHEDULE_RANGE",
                $"EndTime ({Schedule.EndTime:O}) must be greater than StartTime ({Schedule.StartTime:O}).",
                nameof(Schedule));
        }

        if (Schedule.Duration < TimeSpan.FromMinutes(30) || Schedule.Duration > TimeSpan.FromHours(12))
        {
            throw new DomainInvariantException(
                "INVALID_SCHEDULE_DURATION",
                "Reservation duration must be between 30 minutes and 12 hours.",
                nameof(Schedule));
        }
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn về mặt thanh toán và tiền cọc.
    /// </summary>
    private void ValidatePayment()
    {
        if (Payment == null)
        {
            throw new DomainInvariantException("INVALID_PAYMENT", "Payment policy must not be null.", nameof(Payment));
        }

        if (Payment.TotalPrice < 0)
        {
            throw new DomainInvariantException(
                "INVALID_TOTAL_PRICE",
                "TotalPrice must be greater than or equal to 0.",
                nameof(Payment.TotalPrice));
        }

        if (Payment.DepositAmount < 0 || Payment.DepositAmount > Payment.TotalPrice)
        {
            throw new DomainInvariantException(
                "DEPOSIT_EXCEEDS_TOTAL_PRICE",
                $"DepositAmount ({Payment.DepositAmount}) must be between 0 and TotalPrice ({Payment.TotalPrice}).",
                nameof(Payment.DepositAmount));
        }
    }

    /// <summary>
    /// Đánh giá xem đơn đặt sân có đủ điều kiện hủy mà không phải chịu phạt tài chính hay không.
    /// QUY ƯỚC NGHIỆP VỤ: Áp dụng Reference Baseline Policy cho mô hình sân thể thao (mặc định ít nhất 24 giờ trước giờ bắt đầu).
    /// Tham số customWindow cho phép cấu hình linh hoạt cửa sổ hủy của từng chính sách cụ thể.
    /// </summary>
    /// <param name="currentUtc">Thời điểm yêu cầu hủy tính theo chuẩn UTC.</param>
    /// <param name="customWindow">Cửa sổ thời gian hủy tùy chỉnh (Nếu null, mặc định áp dụng 24 giờ).</param>
    /// <returns>True nếu thời điểm hủy cách giờ bắt đầu ít nhất bằng cửa sổ quy định; ngược lại trả về False.</returns>
    public bool CanBeCancelledWithoutPenalty(DateTime currentUtc, TimeSpan? customWindow = null)
    {
        var window = customWindow ?? TimeSpan.FromHours(24);
        return (Schedule.StartTime - currentUtc) >= window;
    }

    /// <summary>
    /// Tính toán tỷ lệ tiền cọc so với tổng giá trị đơn đặt sân (Computed Property).
    /// QUY ƯỚC NGHIỆP VỤ (Business Convention): Khi TotalPrice == 0 (đơn đặt sân miễn phí / tài trợ),
    /// tỷ lệ cọc trả về là 1.0m (100%) đại diện cho việc nghĩa vụ thanh toán cọc đã được thỏa mãn đầy đủ.
    /// </summary>
    /// <returns>Tỷ lệ từ 0.0m đến 1.0m.</returns>
    public decimal CalculateDepositRatio()
    {
        if (Payment.TotalPrice == 0)
        {
            return 1.0m;
        }

        return Payment.DepositAmount / Payment.TotalPrice;
    }
}
