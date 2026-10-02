using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// UT-02: ánh xạ <c>DecisionRequestDto</c> → <c>Reservation</c> — vi phạm hình thức mà schema diễn đạt được (múi giờ không
/// phải IANA, khách vãng lai kèm customerId, currency sai...) → <see cref="ContractValidationException"/> (B-06);
/// vi phạm ngoài schema (khoảng thời gian item) → <see cref="DomainInvariantException"/>.
/// </summary>
public class DecisionRequestMappingTests
{
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_IntegrationSpecSample_MapsAllFields()
    {
        var reservation = V2Samples.DeserializeRequest(V2Samples.RequestJson).ToDomain();

        reservation.BranchTimeZone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(7));
        reservation.Items.Should().ContainSingle();
        reservation.EarliestStart.Should().Be(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)));
        reservation.Customer.Tier.Should().Be(CustomerTier.GOLD);
        reservation.Customer.MemberSince.Should().Be(new DateTimeOffset(2025, 3, 14, 8, 0, 0, TimeSpan.FromHours(7)));
        V2Samples.Required(reservation.Customer.History).LateCancelledCount.Should().Be(1);
        V2Samples.Required(reservation.Demand).WaitlistCount.Should().Be(2);
        reservation.Metadata["promotionCode"].Should().Be("WINTER");
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("SE Asia Standard Time")]
    [InlineData("Mars/Base")]
    [InlineData("")]
    [InlineData(" ")]
    public void ToDomain_NonIanaTimeZone_ThrowsContractValidation(string timeZone)
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root => root["branchTimeZone"] = timeZone));

        var act = () => dto.ToDomain();

        act.Should().Throw<ContractValidationException>().Which.ErrorCode.Should().Be("INVALID_TIME_ZONE");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_GuestWithoutIdentity_MapsGuest()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("customerId");
            customer.Remove("memberSince");
            customer["isGuest"] = true;
            customer["tier"] = "STANDARD";
        }));

        var reservation = dto.ToDomain();

        reservation.Customer.IsGuest.Should().BeTrue();
        reservation.Customer.CustomerId.Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_GuestWithCustomerId_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("memberSince");
            customer["isGuest"] = true;
        }));

        var act = () => dto.ToDomain();

        var exception = act.Should().Throw<ContractValidationException>().Which;
        exception.ErrorCode.Should().Be("INVALID_CUSTOMER");
        exception.FieldName.Should().Be("customer.customerId");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_EndBeforeStart_ThrowsDomainInvariant()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
            V2Samples.Object(root["items"]?[0])["endTime"] = "2026-11-20T17:00:00+07:00"));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_RANGE");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_OptionalSignalsOmitted_UsesDefaults()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
        {
            root.Remove("demand");
            var history = V2Samples.Object(V2Samples.Object(root["customer"])["history"]);
            history.Remove("lateCancelledCount");
            history.Remove("paymentExpiredCount");
        }));

        var reservation = dto.ToDomain();

        reservation.Demand.Should().BeNull();
        V2Samples.Required(reservation.Customer.History).LateCancelledCount.Should().Be(0);
        V2Samples.Required(reservation.Customer.History).PaymentExpiredCount.Should().Be(0);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_NullCustomer_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root => root["customer"] = null));

        var act = () => dto.ToDomain();

        act.Should().Throw<ContractValidationException>().Which.ErrorCode.Should().Be("INVALID_CUSTOMER");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_NullItemsArray_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root => root["items"] = null));

        var act = () => dto.ToDomain();

        act.Should().Throw<ContractValidationException>().Which.ErrorCode.Should().Be("INVALID_ITEMS");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_NullItemElement_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
            V2Samples.Object(root)["items"] = new System.Text.Json.Nodes.JsonArray(null, null)));

        var act = () => dto.ToDomain();

        act.Should().Throw<ContractValidationException>().Which.ErrorCode.Should().Be("INVALID_ITEMS");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_NullMetadataValue_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root =>
            V2Samples.Object(root["metadata"])["promotionCode"] = null));

        var act = () => dto.ToDomain();

        var exception = act.Should().Throw<ContractValidationException>().Which;
        exception.ErrorCode.Should().Be("INVALID_METADATA");
        exception.FieldName.Should().Be("metadata");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_NullCurrency_ThrowsContractValidation()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.MutateRequest(root => root["currency"] = null));

        var act = () => dto.ToDomain();

        var exception = act.Should().Throw<ContractValidationException>().Which;
        exception.ErrorCode.Should().Be("INVALID_CURRENCY");
        exception.FieldName.Should().Be("currency");
    }
}
