using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.Enums;
using TADSS.UnitTests.V2.TestData;
using Xunit;

namespace TADSS.UnitTests.V2.Contracts;

/// <summary>
/// UT-02: serialization contract v2 — round-trip không mất dữ liệu; trường lạ, thiếu trường bắt buộc, enum lạ/số,
/// thời gian thiếu offset bị từ chối; trường <c>deposit</c>/<c>approvalExpiresAt</c> luôn có mặt trên wire.
/// </summary>
public class DecisionWireContractTests
{
    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_IntegrationSpecSample_RoundTripsWithoutLoss()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.RequestJson);

        var roundTrip = JsonSerializer.Serialize(dto, TadssJson.Options);

        JsonNode.DeepEquals(JsonNode.Parse(V2Samples.RequestJson), JsonNode.Parse(roundTrip)).Should().BeTrue(roundTrip);
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_IntegrationSpecSample_DeserializesAllFields()
    {
        var dto = V2Samples.DeserializeRequest(V2Samples.RequestJson);

        dto.Items.Should().ContainSingle();
        dto.Items[0].StartTime.Should().Be(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)));
        dto.Customer.Tier.Should().Be(CustomerTier.GOLD);
        V2Samples.Required(dto.Customer.History).LateCancelledCount.Should().Be(1);
        V2Samples.Required(dto.Demand).OccupancyRate.Should().Be(0.75m);
        dto.Channel.Should().Be(Channel.ONLINE);
        dto.Metadata.Should().ContainKey("promotionCode");
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("extra-root")]
    [InlineData("extra-nested")]
    [InlineData("channel-number")]
    [InlineData("channel-numeric-string")]
    [InlineData("channel-lowercase")]
    [InlineData("channel-unknown")]
    [InlineData("tier-unknown")]
    [InlineData("missing-currency")]
    [InlineData("start-without-offset")]
    [InlineData("price-as-string")]
    public void Request_InvalidWire_ThrowsJsonException(string caseName)
    {
        var json = V2Samples.MutateRequest(root =>
        {
            var item = V2Samples.Object(root["items"]?[0]);
            var customer = V2Samples.Object(root["customer"]);
            switch (caseName)
            {
                case "extra-root": root["unexpected"] = 1; break;
                case "extra-nested": item["unexpected"] = 1; break;
                case "channel-number": root["channel"] = 1; break;
                case "channel-numeric-string": root["channel"] = "1"; break;
                case "channel-lowercase": root["channel"] = "online"; break;
                case "channel-unknown": root["channel"] = "PHONE"; break;
                case "tier-unknown": customer["tier"] = "VIP"; break;
                case "missing-currency": root.Remove("currency"); break;
                case "start-without-offset": item["startTime"] = "2026-11-20T18:00:00"; break;
                case "price-as-string": root["totalPrice"] = "228000"; break;
                default: throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null);
            }
        });

        var act = () => V2Samples.DeserializeRequest(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Request_OptionalSignalsOmitted_DeserializesWithDefaults()
    {
        var json = V2Samples.MutateRequest(root =>
        {
            root.Remove("demand");
            root.Remove("metadata");
            var customer = V2Samples.Object(root["customer"]);
            customer.Remove("memberSince");
            var history = V2Samples.Object(customer["history"]);
            history.Remove("lateCancelledCount");
            history.Remove("paymentExpiredCount");
            history.Remove("lastCompletedAt");
        });

        var dto = V2Samples.DeserializeRequest(json);

        dto.Demand.Should().BeNull();
        dto.Metadata.Should().BeNull();
        dto.Customer.MemberSince.Should().BeNull();
        V2Samples.Required(dto.Customer.History).LateCancelledCount.Should().Be(0);
        V2Samples.Required(dto.Customer.History).PaymentExpiredCount.Should().Be(0);
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("2026-11-18T09:15:00+07:00")]
    [InlineData("2026-11-18T02:15:00Z")]
    [InlineData("2026-11-18T02:15:00.041Z")]
    public void Request_RequestedAtWithOffset_IsAccepted(string requestedAt)
    {
        var json = V2Samples.MutateRequest(root => root["requestedAt"] = requestedAt);

        V2Samples.DeserializeRequest(json).RequestedAt.UtcDateTime.Date.Should().Be(new DateTime(2026, 11, 18));
    }

    [Theory]
    [Trait("CatalogId", "UT-02")]
    [InlineData("2026-11-18T09:15:00")]
    [InlineData("2026-11-18")]
    [InlineData("18/11/2026 09:15")]
    [InlineData("2026-11-18T09:15:00+0700")]
    [InlineData("2026-11-18T09:15:00+07:00\n")]
    [InlineData("2026-11-18T02:15:00Z\n")]
    [InlineData("٢٠٢٦-11-18T09:15:00+07:00")]
    public void Request_RequestedAtWithoutExplicitOffset_ThrowsJsonException(string requestedAt)
    {
        var json = V2Samples.MutateRequest(root => root["requestedAt"] = requestedAt);

        var act = () => V2Samples.DeserializeRequest(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Result_IntegrationSpecSample_Deserializes()
    {
        var result = JsonSerializer.Deserialize<DecisionResultDto>(V2Samples.ResultJson, TadssJson.Options)
            ?? throw new InvalidOperationException("Deserialize trả null.");

        result.Status.Should().Be(DecisionStatus.APPROVED);
        V2Samples.Required(result.Deposit).Amount.Should().Be(114_000m);
        result.ApprovalExpiresAt.Should().BeNull();
        result.EvaluatedAt.Should().Be(new DateTimeOffset(2026, 11, 18, 2, 15, 0, 41, TimeSpan.Zero));
    }

    [Fact]
    [Trait("CatalogId", "UT-02")]
    public void Result_NeedApproval_SerializesNullDepositAndExpiry()
    {
        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = Guid.NewGuid(),
            Status = DecisionStatus.NEED_APPROVAL,
            Deposit = null,
            StrategyName = "DefaultDecisionStrategy",
            StrategyVersion = "2.0.0",
            ExecutionType = ExecutionType.AUTOMATED,
            FallbackUsed = false,
            ReasonCode = ReasonCodes.HighValueReview,
            Reasons = ["Giá trị lớn."],
            Recommendations = [new DecisionRecommendationDto { Type = RecommendationType.REQUIRE_APPROVAL, Description = "Manager xem xét." }],
            ApprovalExpiresAt = new DateTimeOffset(2026, 11, 18, 2, 45, 0, TimeSpan.Zero),
            EvaluatedAt = new DateTimeOffset(2026, 11, 18, 2, 15, 0, TimeSpan.Zero)
        };

        var root = V2Samples.Object(JsonNode.Parse(JsonSerializer.Serialize(result, TadssJson.Options)));

        root.ContainsKey("deposit").Should().BeTrue();
        root["deposit"].Should().BeNull();
        V2Samples.Required(root["approvalExpiresAt"]).GetValue<string>().Should().Be("2026-11-18T02:45:00+00:00");
        V2Samples.Required(root["status"]).GetValue<string>().Should().Be("NEED_APPROVAL");
    }
}
