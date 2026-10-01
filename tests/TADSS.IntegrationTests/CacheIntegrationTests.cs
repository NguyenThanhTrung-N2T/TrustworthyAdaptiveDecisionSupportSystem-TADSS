using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Shared.DecisionContractSDK.DTOs.Decisions;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests;

[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class CacheIntegrationTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CacheIntegrationTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_DifferentIdempotencyKeysSamePayload_SecondRequestIsCacheHitWithNewDecisionId()
    {
        // Arrange: Prepare identical request payload
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            BookingType = "Single",
            TotalPrice = 500000m,
            DepositAmount = 500000m,
            PaymentStatus = "PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        // Act 1: First request with Key A -> Cache Miss (Evaluates strategy & writes to cache)
        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg1.Headers.Add("X-Idempotency-Key", "cache-test-key-01");
        var res1 = await Client.SendAsync(msg1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision1 = await res1.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        decision1.Should().NotBeNull();

        // Act 2: Second request with exact SAME payload but DIFFERENT idempotency Key B -> Cache Hit
        var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg2.Headers.Add("X-Idempotency-Key", "cache-test-key-02");
        var res2 = await Client.SendAsync(msg2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision2 = await res2.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        decision2.Should().NotBeNull();

        // Assert 1: Decisions have the same status but DIFFERENT DecisionId
        decision2!.Status.Should().Be(decision1!.Status);
        decision2.DecisionId.Should().NotBe(decision1.DecisionId); // Must have a fresh DecisionId

        // Assert 2: GET detail on second decision verifies it was a CACHE_HIT
        var detailRes2 = await Client.GetAsync($"/api/v1/decisions/{decision2.DecisionId}");
        detailRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail2 = await detailRes2.Content.ReadFromJsonAsync<DecisionDetailDto>(JsonOptions);
        detail2.Should().NotBeNull();
        detail2!.Execution.ExecutionType.Should().Be("CACHE_HIT");
        detail2.Execution.CacheHit.Should().BeTrue();
        detail2.Execution.FallbackUsed.Should().BeFalse();

        // Assert 3: GET detail on first decision verifies it was AUTOMATED
        var detailRes1 = await Client.GetAsync($"/api/v1/decisions/{decision1.DecisionId}");
        detailRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail1 = await detailRes1.Content.ReadFromJsonAsync<DecisionDetailDto>(JsonOptions);
        detail1.Should().NotBeNull();
        detail1!.Execution.ExecutionType.Should().Be("AUTOMATED");
        detail1.Execution.CacheHit.Should().BeFalse();
    }

    [Fact]
    [Trait("CatalogId", "IT-13")]
    public async Task PostDecision_SameIdempotencyKeySamePayload_TakesPrecedenceAsIdempotencyReplayOverCacheHit()
    {
        // Arrange
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "Single",
            TotalPrice = 600000m,
            DepositAmount = 600000m,
            PaymentStatus = "PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var key = "idemp-over-cache-key";

        // First request
        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg1.Headers.Add("X-Idempotency-Key", key);
        var res1 = await Client.SendAsync(msg1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision1 = await res1.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);

        // Second request with SAME key
        var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg2.Headers.Add("X-Idempotency-Key", key);
        var res2 = await Client.SendAsync(msg2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision2 = await res2.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);

        // Assert: Idempotency replay returns the SAME DecisionId (Replay snapshot, no new decision minted)
        decision2!.DecisionId.Should().Be(decision1!.DecisionId);
    }
}
