using FluentAssertions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.V2.Configuration;
using TADSS.Runtime.Api.V2.Services.Decisions;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Runtime.Services;

/// <summary>
/// P-01: chính sách thời lượng item (<see cref="ItemDurationPolicy"/>) — giới hạn bao gồm hai biên, báo đúng chỉ số
/// item vi phạm và tính thời lượng theo instant (không theo giờ địa phương).
/// </summary>
public class ItemDurationPolicyTests
{
    private static readonly DateTimeOffset Start = V2TestData.FridayPeakStart;

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(720, true)]
    [InlineData(721, false)]
    public void EnsureWithinLimits_Boundaries_Theory(int minutes, bool isValid)
    {
        var policy = CreatePolicy();
        var reservation = CreateReservation(Item(Start, minutes));

        var act = () => policy.EnsureWithinLimits(reservation);

        if (isValid)
        {
            act.Should().NotThrow();
        }
        else
        {
            var exception = act.Should().Throw<DomainInvariantException>().Which;
            exception.InvariantCode.Should().Be("INVALID_ITEM_DURATION");
            exception.FieldName.Should().Be("items[0]");
            exception.Message.Should().Contain("30").And.Contain("720");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void EnsureWithinLimits_SecondItemTooLong_ReportsIndex()
    {
        var policy = CreatePolicy();
        var reservation = CreateReservation(Item(Start, 60), Item(Start.AddDays(1), 721));

        var act = () => policy.EnsureWithinLimits(reservation);

        act.Should().Throw<DomainInvariantException>().Which.FieldName.Should().Be("items[1]");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void EnsureWithinLimits_CustomLimits_AreApplied()
    {
        var policy = CreatePolicy(min: 60, max: 120);

        var tooShort = () => policy.EnsureWithinLimits(CreateReservation(Item(Start, 59)));
        var tooLong = () => policy.EnsureWithinLimits(CreateReservation(Item(Start, 121)));
        var inside = () => policy.EnsureWithinLimits(CreateReservation(Item(Start, 60), Item(Start.AddDays(1), 120)));

        tooShort.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_DURATION");
        tooLong.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_DURATION");
        inside.Should().NotThrow();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void EnsureWithinLimits_ItemAcrossDstChange_UsesInstantDuration()
    {
        // Lisbon chuyển sang giờ mùa hè lúc 01:00Z ngày 29/03/2026: 00:30Z → 02:30Z là 2 giờ instant
        // nhưng đồng hồ địa phương chạy từ 00:30 đến 03:30 (chênh 3 giờ).
        var start = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);
        var end = TimeZoneInfo.ConvertTime(start.AddHours(2), V2TestData.Lisbon);
        (end.DateTime - start.DateTime).Should().Be(TimeSpan.FromHours(3), "tiền đề: đồng hồ địa phương lệch instant do đổi giờ");
        var item = new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), start, end, 240_000m);
        var reservation = V2TestData.CreateReservation(items: [item], timeZone: V2TestData.Lisbon);

        var exactTwoHours = () => CreatePolicy(min: 120, max: 120).EnsureWithinLimits(reservation);
        var wallClockThreeHours = () => CreatePolicy(min: 180, max: 180).EnsureWithinLimits(reservation);

        exactTwoHours.Should().NotThrow("thời lượng tính trên instant là đúng 120 phút");
        wallClockThreeHours.Should().Throw<DomainInvariantException>("180 phút theo đồng hồ địa phương không phải thời lượng thật");
    }

    private static ItemDurationPolicy CreatePolicy(int min = 30, int max = 720) =>
        new(Options.Create(new ValidationOptions { MinItemMinutes = min, MaxItemMinutes = max }));

    private static ReservationItem Item(DateTimeOffset start, int minutes) =>
        new(Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(minutes), 240_000m);

    private static Reservation CreateReservation(params ReservationItem[] items) =>
        V2TestData.CreateReservation(items: items);
}
