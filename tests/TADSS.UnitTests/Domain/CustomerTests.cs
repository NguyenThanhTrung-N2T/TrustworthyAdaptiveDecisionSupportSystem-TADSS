using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Domain;

/// <summary>
/// UT-01: bất biến của <see cref="CustomerProfile"/>, <see cref="CustomerHistory"/> và <see cref="DemandSnapshot"/>
/// (khách vãng lai không có định danh và thâm niên; số đếm không âm; tỷ lệ kín sân trong [0, 1]).
/// </summary>
public class CustomerTests
{
    private static readonly DateTimeOffset MemberSince = new(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_RegisteredMember_ExposesValues()
    {
        var customerId = Guid.NewGuid();

        var profile = new CustomerProfile(false, customerId, CustomerTier.SILVER, MemberSince, null);

        profile.IsGuest.Should().BeFalse();
        profile.CustomerId.Should().Be(customerId);
        profile.Tier.Should().Be(CustomerTier.SILVER);
        profile.MemberSince.Should().Be(MemberSince);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void Constructor_GuestWithoutIdentity_IsAllowed()
    {
        var profile = new CustomerProfile(true, null, CustomerTier.STANDARD, null, null);

        profile.IsGuest.Should().BeTrue();
        profile.CustomerId.Should().BeNull();
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("guest-with-id", "GUEST_WITH_CUSTOMER_ID")]
    [InlineData("guest-with-member-since", "GUEST_WITH_MEMBER_SINCE")]
    [InlineData("member-without-id", "MISSING_CUSTOMER_ID")]
    [InlineData("member-with-empty-id", "MISSING_CUSTOMER_ID")]
    [InlineData("undefined-tier", "INVALID_CUSTOMER_TIER")]
    public void Constructor_InvalidProfile_ThrowsInvariant(string caseName, string expectedCode)
    {
        Action act = caseName switch
        {
            "guest-with-id" => () => _ = new CustomerProfile(true, Guid.NewGuid(), CustomerTier.STANDARD, null, null),
            "guest-with-member-since" => () => _ = new CustomerProfile(true, null, CustomerTier.STANDARD, MemberSince, null),
            "member-without-id" => () => _ = new CustomerProfile(false, null, CustomerTier.GOLD, MemberSince, null),
            "member-with-empty-id" => () => _ = new CustomerProfile(false, Guid.Empty, CustomerTier.GOLD, MemberSince, null),
            "undefined-tier" => () => _ = new CustomerProfile(false, Guid.NewGuid(), (CustomerTier)42, MemberSince, null),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void History_NegativeCount_ThrowsInvalidCustomerHistory(
        int completed, int noShow, int cancelled, int lateCancelled, int paymentExpired)
    {
        var act = () => new CustomerHistory(completed, noShow, cancelled, lateCancelled, paymentExpired, null);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_CUSTOMER_HISTORY");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Demand_BoundaryOccupancy_IsAllowed(double occupancyRate)
    {
        var demand = new DemandSnapshot((decimal)occupancyRate, 0);

        demand.OccupancyRate.Should().Be((decimal)occupancyRate);
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData(-0.01, 0, "INVALID_OCCUPANCY_RATE")]
    [InlineData(1.01, 0, "INVALID_OCCUPANCY_RATE")]
    [InlineData(0.5, -1, "INVALID_WAITLIST_COUNT")]
    public void Demand_OutOfRange_ThrowsInvariant(double occupancyRate, int waitlistCount, string expectedCode)
    {
        var act = () => new DemandSnapshot((decimal)occupancyRate, waitlistCount);

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(expectedCode);
    }
}
