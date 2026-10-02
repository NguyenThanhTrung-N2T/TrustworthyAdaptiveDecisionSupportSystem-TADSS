using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="Reservation"/> v2 (1..20 item, định danh, tổng tiền, tiền tệ, kênh)
/// và tính bất biến sau khi khởi tạo.
/// </summary>
public class ReservationTests
{
    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ValidItems_CopiesItemsAndComputesEarliestStart()
    {
        var later = V2TestData.CreateItem(V2TestData.FridayPeakStart.AddHours(2));
        var earlier = V2TestData.CreateItem(V2TestData.FridayPeakStart);
        var source = new List<ReservationItem> { later, earlier };

        var reservation = V2TestData.CreateReservation(items: source);
        source.Add(V2TestData.CreateItem(V2TestData.FridayPeakStart.AddHours(4)));

        reservation.Items.Should().HaveCount(2, "danh sách item được sao chép khi khởi tạo");
        reservation.EarliestStart.Should().Be(V2TestData.FridayPeakStart);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Constructor_ItemCount_EnforcesOneToTwenty(int count, bool isValid)
    {
        var items = Enumerable.Range(0, count)
            .Select(index => V2TestData.CreateItem(V2TestData.FridayPeakStart.AddDays(index)))
            .ToArray();

        var act = () => V2TestData.CreateReservation(items: items);

        if (isValid)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_COUNT");
        }
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NullItem_ThrowsInvalidItemCount()
    {
        // Cast không kiểm tra null nên mảng kết quả giữ phần tử null mà không cần toán tử '!'
        var items = new ReservationItem?[] { V2TestData.CreateItem(V2TestData.FridayPeakStart), null }
            .Cast<ReservationItem>()
            .ToArray();

        var act = () => V2TestData.CreateReservation(items: items);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_COUNT");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(true, "INVALID_RESERVATION_ID")]
    [InlineData(false, "INVALID_BRANCH_ID")]
    public void Constructor_EmptyIdentifier_ThrowsInvariant(bool emptyReservation, string expectedCode)
    {
        var act = () => V2TestData.CreateReservation(
            reservationId: emptyReservation ? Guid.Empty : Guid.NewGuid(),
            branchId: emptyReservation ? Guid.NewGuid() : Guid.Empty);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NegativeTotalPrice_ThrowsInvalidTotalPrice()
    {
        var act = () => V2TestData.CreateReservation(totalPrice: -1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_TOTAL_PRICE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ZeroTotalPrice_IsAllowed()
    {
        var act = () => V2TestData.CreateReservation(totalPrice: 0m);

        act.Should().NotThrow();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("vnd")]
    [InlineData("VN")]
    [InlineData("VNDX")]
    [InlineData("")]
    [InlineData("V1D")]
    [InlineData("VND\n")]
    public void Constructor_InvalidCurrency_ThrowsInvalidCurrency(string currency)
    {
        var act = () => V2TestData.CreateReservation(currency: currency);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CURRENCY");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_UndefinedChannel_ThrowsInvalidChannel()
    {
        var act = () => V2TestData.CreateReservation(channel: (Channel)7);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CHANNEL");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_Metadata_IsCopiedAndDefaultsToEmpty()
    {
        var source = new Dictionary<string, string> { ["promotionCode"] = "WINTER" };

        var withMetadata = V2TestData.CreateReservation(metadata: source);
        source["promotionCode"] = "CHANGED";
        var withoutMetadata = V2TestData.CreateReservation();

        withMetadata.Metadata["promotionCode"].Should().Be("WINTER");
        withoutMetadata.Metadata.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Items_AndMetadata_AreNotWritableCollections()
    {
        var reservation = V2TestData.CreateReservation(metadata: new Dictionary<string, string> { ["promotionCode"] = "WINTER" });

        reservation.Items.Should().NotBeAssignableTo<ReservationItem[]>("downcast sang mảng sẽ cho phép ghi đè item");
        reservation.Metadata.Should().NotBeAssignableTo<Dictionary<string, string>>("downcast sang Dictionary sẽ cho phép ghi đè metadata");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void ToBranchLocal_UtcInstant_ConvertsToBranchTimeZone()
    {
        var reservation = V2TestData.CreateReservation();

        var local = reservation.ToBranchLocal(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero));

        local.Offset.Should().Be(TimeSpan.FromHours(7));
        local.Hour.Should().Be(18);
    }
}
