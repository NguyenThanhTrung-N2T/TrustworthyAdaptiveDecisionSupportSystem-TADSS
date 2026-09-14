namespace Shared.DecisionContractSDK.Domain;

public record ReservationSchedule
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }

    public TimeSpan Duration => EndTime - StartTime;

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

        StartTime = startTime;
        EndTime = endTime;
    }
}
