using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using Xunit;

namespace TADSS.IntegrationTests;

public class DecisionsEndpointTests : IClassFixture<TadssWebApplicationFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DecisionsEndpointTests(TadssWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostDecision_ValidRequest_Returns200WithHeadersAndResult()
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

        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", "integration-idemp-001");
        message.Headers.Add("X-Correlation-ID", "custom-trace-001");

        // Act
        var response = await _client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-ID")
            .WhoseValue.First().Should().Be("custom-trace-001");
        response.Headers.Should().ContainKey("X-Decision-ID");

        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.Status.Should().Be("APPROVED");
        result.ReservationId.Should().Be(request.ReservationId);
    }

    [Fact]
    public async Task PostDecision_MissingIdempotencyHeader_Returns400ProblemDetails()
    {
        // Arrange
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(2),
            BookingType = "Single",
            TotalPrice = 200000m,
            DepositAmount = 100000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        // Omit X-Idempotency-Key header

        // Act
        var response = await _client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Missing Required Header");
    }

    [Fact]
    public async Task GetDecisionDetail_ExistingDecision_Returns200WithAuditDetail()
    {
        // Arrange: Create a decision first
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
            TotalPrice = 300000m,
            DepositAmount = 30000m, // 10% -> NeedApproval
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        postMsg.Headers.Add("X-Idempotency-Key", "integration-idemp-detail");

        var postRes = await _client.SendAsync(postMsg);
        postRes.EnsureSuccessStatusCode();
        var evalResult = await postRes.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);

        // Act: Get decision detail
        var getRes = await _client.GetAsync($"/api/v1/decisions/{evalResult!.DecisionId}");

        // Assert
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await getRes.Content.ReadFromJsonAsync<DecisionDetailDto>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.DecisionId.Should().Be(evalResult.DecisionId);
        detail.Decision.Status.Should().Be("NEED_APPROVAL");
        detail.Governance.NeedApproval.Should().BeTrue();
    }

    [Fact]
    public async Task OverrideDecision_EligibleDecision_Returns200AndUpdatesState()
    {
        // Arrange: Create a NeedApproval decision
        var request = new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = "Standard",
            StartTime = DateTime.UtcNow.AddDays(3),
            EndTime = DateTime.UtcNow.AddDays(3).AddHours(2),
            BookingType = "Single",
            TotalPrice = 300000m,
            DepositAmount = 10000m,
            PaymentStatus = "PARTIALLY_PAID",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        postMsg.Headers.Add("X-Idempotency-Key", "integration-idemp-override");

        var postRes = await _client.SendAsync(postMsg);
        var evalResult = await postRes.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);

        var overrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager-bob",
            OverrideReason = "Khách đặt tổ chức sinh nhật"
        };

        // Act: Override the decision
        var overrideRes = await _client.PostAsJsonAsync(
            $"/api/v1/decisions/{evalResult!.DecisionId}/override", overrideReq);

        // Assert
        overrideRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var overrideResp = await overrideRes.Content.ReadFromJsonAsync<OverrideDecisionResponseDto>(JsonOptions);
        overrideResp.Should().NotBeNull();
        overrideResp!.OriginalStatus.Should().Be("NEED_APPROVAL");
        overrideResp.OverriddenStatus.Should().Be("APPROVED");
        overrideResp.IsOverridden.Should().BeTrue();
        overrideResp.OverriddenBy.Should().Be("manager-bob");
    }

    [Fact]
    public async Task EvaluateDecision_IdempotencyKeyExceeds128Chars_Returns400BadRequest_While128CharsSucceeds()
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
            TotalPrice = 400000m,
            DepositAmount = 400000m,
            PaymentStatus = "PAID",
            Priority = "HIGH",
            RequestedAt = DateTime.UtcNow
        };

        var tooLongKey = new string('k', 129);
        var validMaxKey = new string('m', 128);

        // Act 1: 129 characters -> 400 Bad Request
        using var badMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        badMsg.Headers.Add("X-Idempotency-Key", tooLongKey);
        var badRes = await _client.SendAsync(badMsg);

        // Assert 1
        badRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = await JsonDocument.ParseAsync(await badRes.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR");

        // Act 2: 128 characters -> 200 OK
        using var okMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        okMsg.Headers.Add("X-Idempotency-Key", validMaxKey);
        var okRes = await _client.SendAsync(okMsg);

        // Assert 2
        okRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostDecision_InvalidWireEnum_Returns400BadRequestProblemDetails()
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
            DepositAmount = 150000m,
            PaymentStatus = "INVALID_WIRE_ENUM",
            Priority = "NORMAL",
            RequestedAt = DateTime.UtcNow
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", "idemp-invalid-enum-" + Guid.NewGuid());

        // Act
        var response = await _client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("title").GetString().Should().Be("Contract Validation Error");
        doc.RootElement.GetProperty("code").GetString().Should().Be("INVALID_PAYMENT_STATUS");
        doc.RootElement.GetProperty("errors").GetProperty("PaymentStatus")[0].GetString()
            .Should().Contain("INVALID_WIRE_ENUM");
    }
}
