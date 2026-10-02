using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="ReservationItem"/> — chỉ <c>End &gt; Start</c> là bất biến thời gian của SDK
/// (giới hạn thời lượng là chính sách runtime, P-01).
/// </summary>
public class ReservationItemTests
{
    private static readonly DateTimeOffset Start = V2TestData.FridayPeakStart;

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ValidInput_ExposesValuesAndDuration()
    {
        var resourceId = Guid.NewGuid();
        var resourceTypeId = Guid.NewGuid();

        var item = new ReservationItem(resourceId, resourceTypeId, Start, Start.AddHours(2), 240_000m);

        item.ResourceId.Should().Be(resourceId);
        item.ResourceTypeId.Should().Be(resourceTypeId);
        item.Price.Should().Be(240_000m);
        item.Duration.Should().Be(TimeSpan.FromHours(2));
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0)]
    [InlineData(-30)]
    public void Constructor_EndNotAfterStart_ThrowsInvalidItemRange(int endOffsetMinutes)
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddMinutes(endOffsetMinutes), 1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_RANGE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ZeroPrice_IsAllowed()
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1), 0m);

        act.Should().NotThrow();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NegativePrice_ThrowsInvalidItemPrice()
    {
        var act = () => new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(1), -1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_PRICE");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(true, "INVALID_RESOURCE_ID")]
    [InlineData(false, "INVALID_RESOURCE_TYPE_ID")]
    public void Constructor_EmptyIdentifier_ThrowsInvariant(bool emptyResource, string expectedCode)
    {
        var act = () => new ReservationItem(
            emptyResource ? Guid.Empty : Guid.NewGuid(),
            emptyResource ? Guid.NewGuid() : Guid.Empty,
            Start, Start.AddHours(1), 1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_MultiDayStay_IsAllowedBySdk()
    {
        // P-01: lưu trú nhiều đêm (PT-01 Hotel Booking Demand) không bị SDK chặn
        var item = new ReservationItem(Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddHours(70), 300m);

        item.Duration.Should().Be(TimeSpan.FromHours(70));
    }
}
