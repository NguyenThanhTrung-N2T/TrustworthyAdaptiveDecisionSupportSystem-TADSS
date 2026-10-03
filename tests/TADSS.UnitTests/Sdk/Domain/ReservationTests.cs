using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.TestData;
using Xunit;

namespace TADSS.UnitTests.Sdk.Domain;

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
        var later = DomainTestData.CreateItem(DomainTestData.FridayPeakStart.AddHours(2));
        var earlier = DomainTestData.CreateItem(DomainTestData.FridayPeakStart);
        var source = new List<ReservationItem> { later, earlier };

        var reservation = DomainTestData.CreateReservation(items: source);
        source.Add(DomainTestData.CreateItem(DomainTestData.FridayPeakStart.AddHours(4)));

        reservation.Items.Should().HaveCount(2, "danh sách item được sao chép khi khởi tạo");
        reservation.EarliestStart.Should().Be(DomainTestData.FridayPeakStart);
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
            .Select(index => DomainTestData.CreateItem(DomainTestData.FridayPeakStart.AddDays(index)))
            .ToArray();

        var act = () => DomainTestData.CreateReservation(items: items);

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
        var items = new ReservationItem?[] { DomainTestData.CreateItem(DomainTestData.FridayPeakStart), null }
            .Cast<ReservationItem>()
            .ToArray();

        var act = () => DomainTestData.CreateReservation(items: items);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_COUNT");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(true, "INVALID_RESERVATION_ID")]
    [InlineData(false, "INVALID_BRANCH_ID")]
    public void Constructor_EmptyIdentifier_ThrowsInvariant(bool emptyReservation, string expectedCode)
    {
        var act = () => DomainTestData.CreateReservation(
            reservationId: emptyReservation ? Guid.Empty : Guid.NewGuid(),
            branchId: emptyReservation ? Guid.NewGuid() : Guid.Empty);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_NegativeTotalPrice_ThrowsInvalidTotalPrice()
    {
        var act = () => DomainTestData.CreateReservation(totalPrice: -1m);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_TOTAL_PRICE");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_ZeroTotalPrice_IsAllowed()
    {
        var act = () => DomainTestData.CreateReservation(totalPrice: 0m);

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
        var act = () => DomainTestData.CreateReservation(currency: currency);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CURRENCY");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_UndefinedChannel_ThrowsInvalidChannel()
    {
        var act = () => DomainTestData.CreateReservation(channel: (Channel)7);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CHANNEL");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_Metadata_IsCopiedAndDefaultsToEmpty()
    {
        var source = new Dictionary<string, string> { ["promotionCode"] = "WINTER" };

        var withMetadata = DomainTestData.CreateReservation(metadata: source);
        source["promotionCode"] = "CHANGED";
        var withoutMetadata = DomainTestData.CreateReservation();

        withMetadata.Metadata["promotionCode"].Should().Be("WINTER");
        withoutMetadata.Metadata.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Items_AndMetadata_AreNotWritableCollections()
    {
        var reservation = DomainTestData.CreateReservation(metadata: new Dictionary<string, string> { ["promotionCode"] = "WINTER" });

        reservation.Items.Should().NotBeAssignableTo<ReservationItem[]>("downcast sang mảng sẽ cho phép ghi đè item");
        reservation.Metadata.Should().NotBeAssignableTo<Dictionary<string, string>>("downcast sang Dictionary sẽ cho phép ghi đè metadata");
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void ToBranchLocal_UtcInstant_ConvertsToBranchTimeZone()
    {
        var reservation = DomainTestData.CreateReservation();

        var local = reservation.ToBranchLocal(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero));

        local.Offset.Should().Be(TimeSpan.FromHours(7));
        local.Hour.Should().Be(18);
    }
}
