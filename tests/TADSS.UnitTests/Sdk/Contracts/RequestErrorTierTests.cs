using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using TADSS.UnitTests.TestData;
using Xunit;
using Xunit.Abstractions;

namespace TADSS.UnitTests.Sdk.Contracts;

/// <summary>
/// B-06 — phân tầng lỗi request v2: vi phạm mà JSON schema diễn đạt được luôn ra <see cref="ContractValidationException"/>
/// (wire <c>VALIDATION_ERROR</c>) dù bị bắt ở schema (BE) hay ở TADSS; <see cref="DomainInvariantException"/> chỉ còn cho
/// quy tắc ngoài schema (<c>endTime &lt;= startTime</c>, định danh <see cref="Guid.Empty"/>).
/// </summary>
public class RequestErrorTierTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string> FormatViolations => new()
    {
        { "branch-time-zone-empty", "INVALID_TIME_ZONE", "branchTimeZone" },
        { "items-null", "INVALID_ITEMS", "items" },
        { "items-empty", "INVALID_ITEMS", "items" },
        { "items-too-many", "INVALID_ITEMS", "items" },
        { "items-null-element", "INVALID_ITEMS", "items[0]" },
        { "items-negative-price", "INVALID_PRICE", "items[2].price" },
        { "total-price-negative", "INVALID_PRICE", "totalPrice" },
        { "currency-null", "INVALID_CURRENCY", "currency" },
        { "currency-lowercase", "INVALID_CURRENCY", "currency" },
        { "currency-four-letters", "INVALID_CURRENCY", "currency" },
        { "customer-null", "INVALID_CUSTOMER", "customer" },
        { "member-without-customer-id", "INVALID_CUSTOMER", "customer.customerId" },
        { "member-null-customer-id", "INVALID_CUSTOMER", "customer.customerId" },
        { "guest-with-customer-id", "INVALID_CUSTOMER", "customer.customerId" },
        { "guest-with-member-since", "INVALID_CUSTOMER", "customer.memberSince" },
        { "history-completed-negative", "INVALID_HISTORY", "customer.history.completedCount" },
        { "history-no-show-negative", "INVALID_HISTORY", "customer.history.noShowCount" },
        { "history-cancelled-negative", "INVALID_HISTORY", "customer.history.cancelledCount" },
        { "history-late-cancelled-negative", "INVALID_HISTORY", "customer.history.lateCancelledCount" },
        { "history-payment-expired-negative", "INVALID_HISTORY", "customer.history.paymentExpiredCount" },
        { "occupancy-above-one", "INVALID_DEMAND", "demand.occupancyRate" },
        { "occupancy-below-zero", "INVALID_DEMAND", "demand.occupancyRate" },
        { "waitlist-negative", "INVALID_DEMAND", "demand.waitlistCount" },
        { "metadata-null-value", "INVALID_METADATA", "metadata" }
    };

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [MemberData(nameof(FormatViolations))]
    public void ToDomain_FormatViolations_Theory(string caseName, string errorCode, string fieldName)
    {
        var dto = ContractSamples.DeserializeRequest(RequestSchemaNegativeCases.Json(caseName));

        var act = () => dto.ToDomain();

        var exception = act.Should().Throw<ContractValidationException>().Which;
        exception.ErrorCode.Should().Be(errorCode);
        exception.FieldName.Should().Be(fieldName);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void ToDomain_EndNotAfterStart_StillDomainInvariant()
    {
        var dto = ContractSamples.DeserializeRequest(ContractSamples.MutateRequest(root =>
            ContractSamples.Object(root["items"]?[0])["endTime"] = "2026-11-20T18:00:00+07:00"));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("INVALID_ITEM_RANGE");
    }

    [Theory]
    [Trait("CatalogId", "UT-01")]
    [InlineData("reservationId", "INVALID_RESERVATION_ID")]
    [InlineData("branchId", "INVALID_BRANCH_ID")]
    public void ToDomain_EmptyGuidIdentifier_StillDomainInvariant(string property, string invariantCode)
    {
        var dto = ContractSamples.DeserializeRequest(ContractSamples.MutateRequest(root => root[property] = Guid.Empty.ToString()));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be(invariantCode);
    }

    [Fact]
    [Trait("CatalogId", "UT-01")]
    public void ToDomain_EmptyGuidCustomerId_StillDomainInvariant()
    {
        var dto = ContractSamples.DeserializeRequest(ContractSamples.MutateRequest(root =>
            ContractSamples.Object(root["customer"])["customerId"] = Guid.Empty.ToString()));

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>().Which.InvariantCode.Should().Be("MISSING_CUSTOMER_ID");
    }

    [Theory]
    [Trait("CatalogId", "CT-01")]
    [MemberData(nameof(RequestSchemaNegativeCases.All), MemberType = typeof(RequestSchemaNegativeCases))]
    public void SchemaInvalidRequest_NeverMapsToDomain_Theory(string caseName)
    {
        var json = RequestSchemaNegativeCases.Json(caseName);
        ContractSchemas.Evaluate("decision-request.schema.json", json).IsValid.Should().BeFalse("ca âm phải thật sự bị schema từ chối");

        DecisionRequestDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<DecisionRequestDto>(json, TadssJson.Options)
                ?? throw new InvalidOperationException("Deserialize trả null.");
        }
        catch (JsonException exception)
        {
            output.WriteLine($"{caseName}: bị chặn khi deserialize ({exception.GetType().Name}).");
            return;
        }

        var act = () => dto.ToDomain();

        var thrown = act.Should().Throw<ContractValidationException>().Which;
        output.WriteLine($"{caseName}: bị chặn ở ToDomain ({thrown.ErrorCode}, {thrown.FieldName}).");
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void ToDomain_SpecSample_StillMaps()
    {
        var reservation = ContractSamples.DeserializeRequest(ContractSamples.RequestJson).ToDomain();

        reservation.ReservationId.Should().Be(Guid.Parse("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11"));
        reservation.BranchId.Should().Be(Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001"));
        reservation.Items.Should().ContainSingle().Which.Price.Should().Be(240000m);
        reservation.TotalPrice.Should().Be(228000m);
        reservation.Currency.Should().Be("VND");
        reservation.Customer.IsGuest.Should().BeFalse();
        reservation.Customer.CustomerId.Should().Be(Guid.Parse("9d3f1c2a-0000-4000-8000-000000000123"));
        ContractSamples.Required(reservation.Demand).OccupancyRate.Should().Be(0.75m);
        reservation.RequestedAt.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7)));
        reservation.Metadata.Should().ContainKey("promotionCode");
    }
}
