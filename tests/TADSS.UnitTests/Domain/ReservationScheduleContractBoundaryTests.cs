using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Xunit;

namespace TADSS.UnitTests.Domain;

public class ReservationScheduleContractBoundaryTests
{
    public static IEnumerable<object[]> DurationBoundaries()
    {
        var start = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);

        yield return new object[] { start.AddMinutes(29).AddSeconds(59), "INVALID_SCHEDULE_DURATION" };
        yield return new object[] { start.AddMinutes(30), null! };
        yield return new object[] { start.AddHours(12), null! };
        yield return new object[] { start.AddHours(12).AddSeconds(1), "INVALID_SCHEDULE_DURATION" };
    }

    [Theory]
    [MemberData(nameof(DurationBoundaries))]
    public void Constructor_EnforcesInclusiveDurationBoundaries(
        DateTime endTime,
        string? expectedInvariantCode)
    {
        var startTime = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);

        var act = () => new ReservationSchedule(startTime, endTime);

        if (expectedInvariantCode is null)
        {
            var schedule = act();
            schedule.Duration.Should().Be(endTime - startTime);
        }
        else
        {
            act.Should().Throw<DomainInvariantException>()
                .Where(exception => exception.InvariantCode == expectedInvariantCode);
        }
    }

    [Fact]
    public void Constructor_WithSubMinutePrecisionBoundaryBelowMinimum_Rejects()
    {
        var startTime = new DateTime(2026, 9, 15, 10, 0, 0, 0, DateTimeKind.Utc);
        var endTime = startTime.AddMinutes(29).AddSeconds(59).AddMilliseconds(999);

        var act = () => new ReservationSchedule(startTime, endTime);

        act.Should().Throw<DomainInvariantException>()
            .Where(exception => exception.InvariantCode == "INVALID_SCHEDULE_DURATION");
    }

    [Fact]
    public void Constructor_WithSubMinutePrecisionBoundaryAtMinimum_Accepts()
    {
        var startTime = new DateTime(2026, 9, 15, 10, 0, 0, 0, DateTimeKind.Utc);
        var endTime = startTime.AddMinutes(30);

        var schedule = new ReservationSchedule(startTime, endTime);

        schedule.Duration.Should().Be(TimeSpan.FromMinutes(30));
    }
}
