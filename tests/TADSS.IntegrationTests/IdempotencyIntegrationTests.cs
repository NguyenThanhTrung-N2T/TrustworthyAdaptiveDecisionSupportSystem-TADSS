using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Xunit;

namespace TADSS.IntegrationTests;

public class IdempotencyIntegrationTests : IClassFixture<TadssWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IdempotencyIntegrationTests(TadssWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostDecision_SameIdempotencyKeySamePayload_Returns200OKAndReplaysOriginalDecision()
    {
        // Arrange
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
            TotalPrice = 300000m,
            DepositAmount = 300000m,
            PaymentStatus = "PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var key = "idemp-replay-integration-key";

        // Act 1: First request
        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg1.Headers.Add("X-Idempotency-Key", key);
        var res1 = await _client.SendAsync(msg1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await res1.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        body1.Should().NotBeNull();

        // Act 2: Second request with exact SAME key and SAME payload
        var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg2.Headers.Add("X-Idempotency-Key", key);
        var res2 = await _client.SendAsync(msg2);

        // Assert
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await res2.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        body2.Should().NotBeNull();
        body2!.DecisionId.Should().Be(body1!.DecisionId);
        body2.Status.Should().Be(body1.Status);
        body2.EvaluatedAt.Should().Be(body1.EvaluatedAt);
    }

    [Fact]
    public async Task PostDecision_SameIdempotencyKeyDifferentPayload_Returns409Conflict()
    {
        // Arrange
        var baseReq = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "VIP",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 300000m,
            PaymentStatus = "PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var key = "shared-idempotency-key-test";

        // First request
        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(baseReq)
        };
        msg1.Headers.Add("X-Idempotency-Key", key);
        var res1 = await _client.SendAsync(msg1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request with SAME key but DIFFERENT payload (price changed)
        var modifiedReq = new DecisionRequestDto
        {
            ReservationId = baseReq.ReservationId,
            CourtId = baseReq.CourtId,
            BranchId = baseReq.BranchId,
            CustomerId = baseReq.CustomerId,
            CustomerTier = baseReq.CustomerTier,
            StartTime = baseReq.StartTime,
            EndTime = baseReq.EndTime,
            BookingType = baseReq.BookingType,
            TotalPrice = 999999m, // Changed
            DepositAmount = baseReq.DepositAmount,
            PaymentStatus = baseReq.PaymentStatus,
            Priority = baseReq.Priority,
            RequestedAt = baseReq.RequestedAt
        };

        var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(modifiedReq)
        };
        msg2.Headers.Add("X-Idempotency-Key", key);

        // Act
        var res2 = await _client.SendAsync(msg2);

        // Assert
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await res2.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Idempotency Key Reuse Conflict");
    }

    [Fact]
    public async Task PostDecision_ReplayAfterOverride_ReturnsOriginalMachineSnapshot_WhileGetDetailShowsOverridden()
    {
        // Arrange
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "Single",
            TotalPrice = 400000m,
            DepositAmount = 40000m, // NEED_APPROVAL (< 30%)
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var key = "idemp-override-integration-key";

        // Act 1: Initial decision
        var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msg1.Headers.Add("X-Idempotency-Key", key);
        var res1 = await _client.SendAsync(msg1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await res1.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        body1!.Status.Should().Be("NEED_APPROVAL");

        // Act 2: Override to APPROVED via Governance API
        var overrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager-integration",
            OverrideReason = "Khách vip quen"
        };
        var overrideRes = await _client.PostAsJsonAsync($"/api/v1/decisions/{body1.DecisionId}/override", overrideReq);
        overrideRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act 3: Client replays request with same idempotency key
        var msgReplay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        msgReplay.Headers.Add("X-Idempotency-Key", key);
        var resReplay = await _client.SendAsync(msgReplay);

        // Assert 1: Replayed machine decision retains original status
        resReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        var bodyReplay = await resReplay.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        bodyReplay!.DecisionId.Should().Be(body1.DecisionId);
        bodyReplay.Status.Should().Be("NEED_APPROVAL");

        // Assert 2: GET /decisions/{id} returns overridden governance state
        var detailRes = await _client.GetAsync($"/api/v1/decisions/{body1.DecisionId}");
        detailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await detailRes.Content.ReadFromJsonAsync<DecisionDetailDto>(JsonOptions);
        detailBody!.Governance.IsOverridden.Should().BeTrue();
        detailBody.Governance.OverriddenBy.Should().Be("manager-integration");
    }
}
