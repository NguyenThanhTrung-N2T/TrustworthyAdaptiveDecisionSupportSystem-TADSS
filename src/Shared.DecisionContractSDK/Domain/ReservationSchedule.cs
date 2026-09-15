namespace Shared.DecisionContractSDK.Domain;

/// <summary>
/// Value Object đại diện cho khung thời gian đặt sân (Bắt đầu và Kết thúc).
/// Đảm bảo tính toàn vẹn: StartTime &lt; EndTime, thời lượng từ 30 phút đến tối đa 12 giờ.
/// </summary>
public record ReservationSchedule
{
    /// <summary>
    /// Thời điểm bắt đầu sử dụng sân (UTC).
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    /// Thời điểm kết thúc sử dụng sân (UTC).
    /// </summary>
    public DateTime EndTime { get; init; }

    /// <summary>
    /// Tổng thời lượng của phiên đặt sân.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Khởi tạo ReservationSchedule với kiểm tra Invariants khung giờ và thời lượng.
    /// </summary>
    /// <param name="startTime">Thời điểm bắt đầu (UTC).</param>
    /// <param name="endTime">Thời điểm kết thúc (UTC).</param>
    /// <exception cref="DomainInvariantException">Ném ra khi EndTime &lt;= StartTime hoặc thời lượng &lt; 30 phút hoặc &gt; 12 giờ.</exception>
    public ReservationSchedule(DateTime startTime, DateTime endTime)
    {
        if (endTime <= startTime)
        {
            throw new DomainInvariantException(
                "INVALID_SCHEDULE_RANGE",
                $"EndTime ({endTime:O}) must be greater than StartTime ({startTime:O}).",
                nameof(EndTime));
        }

        if (endTime - startTime < TimeSpan.FromMinutes(30))
        {
            throw new DomainInvariantException(
                "INVALID_SCHEDULE_DURATION",
                "Reservation duration must be at least 30 minutes.",
                nameof(EndTime));
        }

        if (endTime - startTime > TimeSpan.FromHours(12))
        {
            throw new DomainInvariantException(
                "INVALID_SCHEDULE_DURATION",
                "Reservation duration cannot exceed 12 hours.",
                nameof(EndTime));
        }

        StartTime = startTime;
        EndTime = endTime;
    }
}
