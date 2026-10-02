using System.Globalization;
using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.V2.Domain;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-05: giờ cao điểm xét theo thời điểm bắt đầu sớm nhất, quy về múi giờ chi nhánh
/// (biên 16:59/17:00/20:59/21:00, cuối tuần, thời điểm UTC đổi ngày, múi giờ khác).
/// </summary>
public class BranchCalendarTests
{
    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("2026-11-20T16:59:00+07:00", false)]
    [InlineData("2026-11-20T17:00:00+07:00", true)]
    [InlineData("2026-11-20T20:59:00+07:00", true)]
    [InlineData("2026-11-20T21:00:00+07:00", false)]
    [InlineData("2026-11-21T07:59:00+07:00", false)]
    [InlineData("2026-11-21T08:00:00+07:00", true)]
    [InlineData("2026-11-22T20:59:00+07:00", true)]
    [InlineData("2026-11-20T10:00:00Z", true)]
    [InlineData("2026-11-20T09:59:00Z", false)]
    [InlineData("2026-11-20T20:00:00Z", false)]
    public void IsPeak_HoChiMinhBranch_UsesBranchLocalTime(string startIso, bool expectedPeak)
    {
        // 2026-11-20T20:00:00Z là thứ Sáu 20:00 theo UTC nhưng là thứ Bảy 03:00 giờ chi nhánh → không cao điểm
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(items: [V2TestData.CreateItem(start, hours: 1)]);

        V2TestData.StandardCalendar().IsPeak(reservation).Should().Be(expectedPeak);
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData("2026-07-03T16:00:00Z", true)]
    [InlineData("2026-07-03T15:59:00Z", false)]
    public void IsPeak_LisbonBranchInSummer_UsesDaylightOffset(string startIso, bool expectedPeak)
    {
        // Lisbon mùa hè UTC+01:00: 16:00Z = 17:00 thứ Sáu giờ chi nhánh
        var start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        var reservation = V2TestData.CreateReservation(
            items: [V2TestData.CreateItem(start, hours: 1)], timeZone: V2TestData.Lisbon, currency: "EUR");

        V2TestData.StandardCalendar().IsPeak(reservation).Should().Be(expectedPeak);
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void IsPeak_MultipleItems_UsesEarliestStartRegardlessOfOrder()
    {
        var offPeak = V2TestData.CreateItem(new DateTimeOffset(2026, 11, 20, 16, 0, 0, V2TestData.VietnamOffset), hours: 1);
        var peak = V2TestData.CreateItem(V2TestData.FridayPeakStart, hours: 1);
        var reservation = V2TestData.CreateReservation(items: [peak, offPeak]);

        V2TestData.StandardCalendar().IsPeak(reservation).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void IsPeak_EmptyCalendar_ReturnsFalse()
    {
        new BranchCalendar([]).IsPeak(V2TestData.CreateReservation()).Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void PeakWindows_AnyCalendar_IsNotWritableArray()
    {
        V2TestData.StandardCalendar().PeakWindows.Should().NotBeAssignableTo<PeakWindow[]>("downcast sang mảng sẽ cho phép ghi đè lịch");
    }

    [Theory]
    [Trait("CatalogId", "UT-05")]
    [InlineData(17, 17)]
    [InlineData(21, 17)]
    public void PeakWindow_EndNotAfterStart_ThrowsInvalidPeakWindow(int startHour, int endHour)
    {
        var act = () => new PeakWindow(DayOfWeek.Friday, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_PEAK_WINDOW");
    }

    [Fact]
    [Trait("CatalogId", "UT-05")]
    public void PeakWindow_UndefinedDay_ThrowsInvalidPeakWindow()
    {
        var act = () => new PeakWindow((DayOfWeek)9, new TimeOnly(17, 0), new TimeOnly(21, 0));

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_PEAK_WINDOW");
    }
}
