using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using Xunit;

namespace TADSS.UnitTests.Contracts;

public class Phase2ContractBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Reservation_DefaultsOptionalValues_AndTrimsCanonicalStrings()
    {
        var reservation = Reservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  GOLD  ",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
            300000m,
            0m,
            PaymentStatus.UNPAID,
            "  ",
            BookingPriority.NORMAL,
            new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc));

        reservation.Customer.Tier.Should().Be("GOLD");
        reservation.BookingType.Should().Be("SINGLE");
        reservation.Metadata.Attributes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(29, 59, "INVALID_SCHEDULE_DURATION")]
    [InlineData(30, 0, null)]
    [InlineData(720, 0, null)]
    [InlineData(720, 1, "INVALID_SCHEDULE_DURATION")]
    public void ReservationSchedule_EnforcesInclusiveDurationBoundaries(
        int minutes,
        int seconds,
        string? expectedInvariantCode)
    {
        var start = new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = start.AddMinutes(minutes).AddSeconds(seconds);

        var act = () => new ReservationSchedule(start, end);

        if (expectedInvariantCode is null)
        {
            act().Duration.Should().Be(end - start);
        }
        else
        {
            act.Should().Throw<DomainInvariantException>()
                .Where(exception => exception.InvariantCode == expectedInvariantCode);
        }
    }

    [Theory]
    [InlineData(0, 0, 100, true)]
    [InlineData(100, 0, 0, false)]
    [InlineData(100, 25, 25, false)]
    [InlineData(100, 100, 100, true)]
    public void PaymentPolicy_CalculatesZeroAndFullPaymentBoundaries(
        decimal totalPrice,
        decimal depositAmount,
        decimal expectedPercentage,
        bool expectedFullyPaid)
    {
        var policy = new PaymentPolicy(totalPrice, depositAmount, PaymentStatus.UNPAID);

        policy.DepositPercentage.Should().Be(expectedPercentage);
        policy.IsFullyPaid.Should().Be(expectedFullyPaid);
    }

    [Fact]
    public void ReservationMetadata_DefensivelyCopiesInputDictionary()
    {
        var source = new Dictionary<string, string> { ["source"] = "MobileApp" };
        var metadata = new ReservationMetadata(source);

        source["source"] = "Changed";
        source["new-key"] = "new-value";

        metadata.Get("source").Should().Be("MobileApp");
        metadata.Get("new-key").Should().BeNull();
        metadata.Attributes.Should().BeAssignableTo<IReadOnlyDictionary<string, string>>();
    }

    [Theory]
    [InlineData(nameof(DecisionRequestDto.ReservationId), "INVALID_RESERVATION_ID")]
    [InlineData(nameof(DecisionRequestDto.CustomerId), "INVALID_CUSTOMER_ID")]
    [InlineData(nameof(DecisionRequestDto.CourtId), "INVALID_COURT_ID")]
    [InlineData(nameof(DecisionRequestDto.BranchId), "INVALID_BRANCH_ID")]
    public void DecisionRequestDto_ToDomain_RejectsEmptyRequiredIdentifiers(
        string propertyName,
        string expectedInvariantCode)
    {
        var dto = CreateValidRequest();
        typeof(DecisionRequestDto).GetProperty(propertyName)!.SetValue(dto, Guid.Empty);

        var act = () => dto.ToDomain();

        act.Should().Throw<DomainInvariantException>()
            .Where(exception => exception.InvariantCode == expectedInvariantCode);
    }

    [Fact]
    public void DecisionRequestDto_DomainRoundTrip_PreservesAllDecisionFields()
    {
        var requestedAt = new DateTime(2026, 9, 12, 15, 0, 0, 123, DateTimeKind.Utc);
        var dto = CreateValidRequest();
        dto.CustomerTier = "VIP";
        dto.BookingType = "RECURRING";
        dto.Priority = "URGENT";
        dto.RequestedAt = requestedAt;
        dto.Metadata = new Dictionary<string, string>
        {
            ["source"] = "MobileApp",
            ["tenant"] = "smashcourt"
        };

        var roundTripped = dto.ToDomain().ToDto();

        roundTripped.Should().BeEquivalentTo(dto);
    }

    [Fact]
    public void DecisionRequestDto_ToDto_ReturnsIndependentMetadataDictionary()
    {
        var dto = CreateValidRequest();
        var domain = dto.ToDomain();

        var mapped = domain.ToDto();
        mapped.Metadata!["source"] = "Changed";

        domain.Metadata.Get("source").Should().Be("MobileApp");
        dto.Metadata!["source"].Should().Be("MobileApp");
    }

    [Theory]
    [MemberData(nameof(CanonicalEnumValues))]
    public void CanonicalEnumValues_ParseWithoutInformationLoss(
        string enumType,
        string value)
    {
        object parsed = enumType switch
        {
            nameof(PaymentStatus) => DecisionMappingExtensions.ParsePaymentStatus(value),
            nameof(BookingPriority) => DecisionMappingExtensions.ParseBookingPriority(value),
            nameof(DecisionStatus) => DecisionMappingExtensions.ParseDecisionStatus(value),
            nameof(RecommendationType) => DecisionMappingExtensions.ParseRecommendationType(value),
            _ => throw new InvalidOperationException($"Unknown enum type: {enumType}")
        };

        parsed.ToString().Should().Be(value);
    }

    public static IEnumerable<object[]> CanonicalEnumValues()
    {
        foreach (var value in Enum.GetValues<PaymentStatus>())
        {
            yield return [nameof(PaymentStatus), value.ToString()];
        }

        foreach (var value in Enum.GetValues<BookingPriority>())
        {
            yield return [nameof(BookingPriority), value.ToString()];
        }

        foreach (var value in Enum.GetValues<DecisionStatus>())
        {
            yield return [nameof(DecisionStatus), value.ToString()];
        }

        foreach (var value in Enum.GetValues<RecommendationType>())
        {
            yield return [nameof(RecommendationType), value.ToString()];
        }
    }

    [Theory]
    [InlineData("paid")]
    [InlineData("PartiallyPaid")]
    [InlineData("partially_paid")]
    [InlineData("1")]
    public void PaymentStatusParser_RejectsNonCanonicalWireValues(string value)
    {
        var act = () => DecisionMappingExtensions.ParsePaymentStatus(value);

        act.Should().Throw<ContractValidationException>()
            .Where(exception => exception.ErrorCode == "INVALID_PAYMENT_STATUS");
    }

    [Fact]
    public void DecisionRequestDto_Serialization_UsesStableWireNamesAndPreservesPrecision()
    {
        var dto = CreateValidRequest();
        dto.TotalPrice = 123456.7891m;
        dto.DepositAmount = 0.0001m;

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("reservationId", out _).Should().BeTrue();
        root.TryGetProperty("paymentStatus", out _).Should().BeTrue();
        root.TryGetProperty("totalPrice", out var totalPrice).Should().BeTrue();
        root.TryGetProperty("depositAmount", out var depositAmount).Should().BeTrue();
        totalPrice.GetDecimal().Should().Be(123456.7891m);
        depositAmount.GetDecimal().Should().Be(0.0001m);
        root.TryGetProperty("ReservationId", out _).Should().BeFalse();
    }

    [Fact]
    public void DecisionRequestDto_DataAnnotations_RejectMissingRequiredStrings()
    {
        var dto = CreateValidRequest();
        dto.BookingType = string.Empty;
        dto.PaymentStatus = string.Empty;

        var results = Validate(dto);

        results.Should().Contain(result => result.MemberNames.Contains(nameof(DecisionRequestDto.BookingType)));
        results.Should().Contain(result => result.MemberNames.Contains(nameof(DecisionRequestDto.PaymentStatus)));
    }

    private static DecisionRequestDto CreateValidRequest() => new()
    {
        ReservationId = Guid.NewGuid(),
        CourtId = Guid.NewGuid(),
        CourtTypeId = Guid.NewGuid(),
        BranchId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        CustomerTier = "GOLD",
        StartTime = new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc),
        EndTime = new DateTime(2026, 9, 15, 20, 0, 0, DateTimeKind.Utc),
        BookingType = "SINGLE",
        TotalPrice = 300000m,
        DepositAmount = 150000m,
        PaymentStatus = "PARTIALLY_PAID",
        Priority = "NORMAL",
        RequestedAt = new DateTime(2026, 9, 12, 15, 0, 0, DateTimeKind.Utc),
        Metadata = new Dictionary<string, string> { ["source"] = "MobileApp" }
    };

    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }
}
