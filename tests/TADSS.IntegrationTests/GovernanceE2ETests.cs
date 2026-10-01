using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using Xunit;

namespace TADSS.IntegrationTests;

[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class GovernanceE2ETests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public GovernanceE2ETests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    #region E2E 1: Correlation ID Propagation (Happy Path & Error Responses)

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task CorrelationId_ClientProvidedCustomString_PropagatedToResponseHeaderAndTraceIdentifier()
    {
        // Arrange
        var customCorrelationId = "CORR-TEST-CUSTOM-999";
        var request = CreateValidRequestDto(depositRatio: 1.0m);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", $"e2e1-corr-happy-{Guid.NewGuid()}");
        message.Headers.Add("X-Correlation-ID", customCorrelationId);

        // Act
        var response = await Client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-ID")
            .WhoseValue.First().Should().Be(customCorrelationId);
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task CorrelationId_ClientProvidedOnBadRequest_PropagatedToProblemDetailsTraceIdAndHeader()
    {
        // Arrange: Missing X-Idempotency-Key triggers 400 BadRequest
        var customCorrelationId = "CORR-ERR-TRACE-400";
        var request = CreateValidRequestDto(depositRatio: 1.0m);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Correlation-ID", customCorrelationId);

        // Act
        var response = await Client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Should().ContainKey("X-Correlation-ID")
            .WhoseValue.First().Should().Be(customCorrelationId);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions.Should().ContainKey("traceId");
        problem.Extensions["traceId"]!.ToString().Should().Be(customCorrelationId);
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task CorrelationId_ClientOmitted_GeneratesValidGuidInResponseHeader()
    {
        // Arrange
        var request = CreateValidRequestDto(depositRatio: 1.0m);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", $"e2e1-corr-omitted-{Guid.NewGuid()}");
        // Omit X-Correlation-ID

        // Act
        var response = await Client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-Correlation-ID");
        var generatedCorrelationId = response.Headers.GetValues("X-Correlation-ID").First();
        Guid.TryParse(generatedCorrelationId, out _).Should().BeTrue();
    }

    #endregion

    #region E2E 2: Missing Idempotency Key Enforcement

    [Fact]
    [Trait("CatalogId", "IT-11")]
    public async Task PostDecision_MissingIdempotencyKey_Returns400WithMissingIdempotencyCode()
    {
        // Arrange
        var request = CreateValidRequestDto(depositRatio: 1.0m);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };

        // Act
        var response = await Client.SendAsync(message);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Missing Required Header");
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be(ErrorCodes.MissingIdempotencyKey);
    }

    #endregion

    #region E2E 3: GET /api/v1/decisions/{decisionId} Detail Verification

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDecisionDetail_ExistingDecision_Returns200WithContractDefinedProjection()
    {
        // Arrange: Evaluate a decision first
        var request = CreateValidRequestDto(depositRatio: 0.1m); // 10% -> NEED_APPROVAL
        var idempotencyKey = $"e2e3-detail-{Guid.NewGuid()}";
        var postMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        postMsg.Headers.Add("X-Idempotency-Key", idempotencyKey);

        var postRes = await Client.SendAsync(postMsg);
        postRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var evalResult = await postRes.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        evalResult.Should().NotBeNull();

        // Act: Get decision detail
        var getRes = await Client.GetAsync($"/api/v1/decisions/{evalResult!.DecisionId}");

        // Assert
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await getRes.Content.ReadFromJsonAsync<DecisionDetailDto>(JsonOptions);
        detail.Should().NotBeNull();
        detail!.DecisionId.Should().Be(evalResult.DecisionId);
        detail.ReservationId.Should().Be(request.ReservationId);
        detail.RequestHash.Should().NotBeNullOrWhiteSpace();

        // Assert: Projection sections exist
        detail.Decision.Should().NotBeNull();
        detail.Decision.Status.Should().Be("NEED_APPROVAL");
        detail.Execution.Should().NotBeNull();
        detail.Execution.StrategyName.Should().NotBeNullOrWhiteSpace();
        detail.Governance.Should().NotBeNull();
        detail.Governance.NeedApproval.Should().BeTrue();
        detail.Governance.IsOverridden.Should().BeFalse();
        detail.Timestamps.Should().NotBeNull();

        // Assert: Raw request payload is NOT exposed in DecisionDetailDto projection
        typeof(DecisionDetailDto).GetProperty("RequestPayload").Should().BeNull();
        typeof(DecisionDetailDto).GetProperty("RawRequest").Should().BeNull();
    }

    [Fact]
    [Trait("CatalogId", "IT-01")]
    public async Task GetDecisionDetail_NonExistentGuid_Returns404ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await Client.GetAsync($"/api/v1/decisions/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Title.Should().Be("Decision Not Found");
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be(ErrorCodes.DecisionNotFound);
    }

    #endregion

    #region E2E 4: Pending Approval Filtering & Pagination

    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task GetPendingApprovals_FiltersOnlyUnOverriddenNeedApprovalRecords()
    {
        // Arrange: Seed 1 APPROVED, 2 NEED_APPROVAL (1 will be overridden)
        var needApproval1 = await PostDecisionAsync(depositRatio: 0.1m, key: $"e2e4-need-1-{Guid.NewGuid()}");
        var needApproval2 = await PostDecisionAsync(depositRatio: 0.1m, key: $"e2e4-need-2-{Guid.NewGuid()}");
        var needApproval3 = await PostDecisionAsync(depositRatio: 0.1m, key: $"e2e4-need-3-{Guid.NewGuid()}");
        var approvedRecord = await PostDecisionAsync(depositRatio: 1.0m, key: $"e2e4-appr-1-{Guid.NewGuid()}");

        // Override needApproval3 so it is now overridden
        var overrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager@smashcourt.vn",
            OverrideReason = "Special concession"
        };
        var overrideRes = await Client.PostAsJsonAsync($"/api/v1/decisions/{needApproval3.DecisionId}/override", overrideReq);
        overrideRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: Query pending approvals
        var getRes = await Client.GetAsync("/api/v1/decisions/pending-approvals?page=1&pageSize=50");

        // Assert
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedResult = await getRes.Content.ReadFromJsonAsync<PagedResult<PendingApprovalItemDto>>(JsonOptions);
        pagedResult.Should().NotBeNull();

        var pendingIds = pagedResult!.Items.Select(i => i.DecisionId).ToList();

        // Un-overridden NeedApproval items must be present
        pendingIds.Should().Contain(needApproval1.DecisionId);
        pendingIds.Should().Contain(needApproval2.DecisionId);

        // Overridden item MUST NOT appear in pending approvals
        pendingIds.Should().NotContain(needApproval3.DecisionId);

        // Fully APPROVED item MUST NOT appear in pending approvals
        pendingIds.Should().NotContain(approvedRecord.DecisionId);
    }

    [Fact]
    [Trait("CatalogId", "IT-09")]
    public async Task GetPendingApprovals_PaginationParameters_ReturnsConsistentPageMetadata()
    {
        // Act: Query with specific page and pageSize
        var response = await Client.GetAsync("/api/v1/decisions/pending-approvals?page=1&pageSize=5");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<PendingApprovalItemDto>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Page.Should().Be(1);
        result.PageSize.Should().Be(5);
        result.TotalItems.Should().BeGreaterOrEqualTo(0);
        result.Items.Should().HaveCountLessThanOrEqualTo(5);
    }

    #endregion

    #region E2E 5: Override Immutability & Duplicate Protection

    [Fact]
    [Trait("CatalogId", "IT-06")]
    public async Task OverrideDecision_PreservesMachineSnapshotImmutability_AndRejectsDuplicates()
    {
        // Arrange: Evaluate a NEED_APPROVAL decision
        var evalResult = await PostDecisionAsync(depositRatio: 0.1m, key: $"e2e5-override-immutability-{Guid.NewGuid()}");
        var decisionId = evalResult.DecisionId;

        // Directly query DB for machine snapshot BEFORE override
        var dbSnapshotBefore = await GetExecutionFromDbAsync(decisionId);
        dbSnapshotBefore.Should().NotBeNull();
        var rawResponsePayloadBefore = dbSnapshotBefore!.ResponsePayload;
        var executionTypeBefore = dbSnapshotBefore.ExecutionType;
        var strategyNameBefore = dbSnapshotBefore.StrategyName;
        var strategyVersionBefore = dbSnapshotBefore.StrategyVersion;
        var executionTimeMsBefore = dbSnapshotBefore.ExecutionTimeMs;

        dbSnapshotBefore.IsOverridden.Should().BeFalse();
        dbSnapshotBefore.OverriddenBy.Should().BeNull();
        dbSnapshotBefore.OverrideReason.Should().BeNull();

        // Act 1: Execute first override
        var overrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "APPROVED" },
            OverriddenBy = "manager@smashcourt.vn",
            OverrideReason = "VIP Customer manual adjustment"
        };

        var overrideRes = await Client.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/override", overrideReq);

        // Assert 1: Override HTTP response is 200 OK
        overrideRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var overrideResp = await overrideRes.Content.ReadFromJsonAsync<OverrideDecisionResponseDto>(JsonOptions);
        overrideResp.Should().NotBeNull();
        overrideResp!.DecisionId.Should().Be(decisionId);
        overrideResp.OriginalStatus.Should().Be("NEED_APPROVAL");
        overrideResp.OverriddenStatus.Should().Be("APPROVED");
        overrideResp.IsOverridden.Should().BeTrue();
        overrideResp.OverriddenBy.Should().Be("manager@smashcourt.vn");
        overrideResp.OverrideReason.Should().Be("VIP Customer manual adjustment");

        // Directly query DB for state AFTER first override
        var dbSnapshotAfterFirst = await GetExecutionFromDbAsync(decisionId);
        dbSnapshotAfterFirst.Should().NotBeNull();

        // ✅ CRITICAL INVARIANT: Machine Snapshot is 100% IMMUTABLE
        dbSnapshotAfterFirst!.ResponsePayload.Should().Be(rawResponsePayloadBefore);
        dbSnapshotAfterFirst.ExecutionType.Should().Be(executionTypeBefore);
        dbSnapshotAfterFirst.StrategyName.Should().Be(strategyNameBefore);
        dbSnapshotAfterFirst.StrategyVersion.Should().Be(strategyVersionBefore);
        dbSnapshotAfterFirst.ExecutionTimeMs.Should().Be(executionTimeMsBefore);

        // Mutable Governance Metadata is updated
        dbSnapshotAfterFirst.IsOverridden.Should().BeTrue();
        dbSnapshotAfterFirst.OverriddenBy.Should().Be("manager@smashcourt.vn");
        dbSnapshotAfterFirst.OverrideReason.Should().Be("VIP Customer manual adjustment");
        dbSnapshotAfterFirst.Version.Should().Be(dbSnapshotBefore.Version + 1);
        // Assert: Overridden decision MUST no longer appear in pending approvals list
        var pendingRes = await Client.GetAsync("/api/v1/decisions/pending-approvals?page=1&pageSize=50");
        pendingRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagedPending = await pendingRes.Content.ReadFromJsonAsync<PagedResult<PendingApprovalItemDto>>(JsonOptions);
        pagedPending.Should().NotBeNull();
        pagedPending!.Items.Should().NotContain(i => i.DecisionId == decisionId);

        // Act 2: Attempt Duplicate Override (Second time on same decision)
        var duplicateOverrideReq = new OverrideDecisionRequestDto
        {
            Decision = new OverrideTargetPayloadDto { Status = "REJECTED" },
            OverriddenBy = "manager2@smashcourt.vn",
            OverrideReason = "Second override attempt"
        };

        var duplicateRes = await Client.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/override", duplicateOverrideReq);

        // Assert 2: Duplicate override is rejected with 400 ProblemDetails
        duplicateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var duplicateProblem = await duplicateRes.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        duplicateProblem.Should().NotBeNull();
        duplicateProblem!.Title.Should().Be("Invalid Operation");

        // ✅ INVARIANT: No additional mutation occurred on failed duplicate override
        var dbSnapshotAfterSecond = await GetExecutionFromDbAsync(decisionId);
        dbSnapshotAfterSecond!.ResponsePayload.Should().Be(rawResponsePayloadBefore);
        dbSnapshotAfterSecond.OverriddenBy.Should().Be("manager@smashcourt.vn"); // still manager 1
        dbSnapshotAfterSecond.OverrideReason.Should().Be("VIP Customer manual adjustment");
    }

    #endregion

    #region Helper Methods

    private async Task<DecisionResultDto> PostDecisionAsync(decimal depositRatio, string key)
    {
        var request = CreateValidRequestDto(depositRatio);
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Idempotency-Key", key);

        var response = await Client.SendAsync(message);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions);
        return result!;
    }

    private static DecisionRequestDto CreateValidRequestDto(decimal depositRatio)
    {
        var totalPrice = 300000m;
        var depositAmount = totalPrice * depositRatio;

        return new DecisionRequestDto
        {
            ReservationId = Guid.NewGuid(),
            CourtId = Guid.NewGuid(),
            BranchId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            CustomerTier = depositRatio < 0.3m ? "STANDARD" : "VIP",
            StartTime = DateTime.UtcNow.AddDays(2),
            EndTime = DateTime.UtcNow.AddDays(2).AddHours(2),
            BookingType = "Single",
            TotalPrice = totalPrice,
            DepositAmount = depositAmount,
            PaymentStatus = depositRatio >= 1.0m ? "PAID" : "PARTIALLY_PAID",
            Priority = depositRatio < 0.3m ? "NORMAL" : "HIGH",
            RequestedAt = DateTime.UtcNow
        };
    }

    private async Task<DecisionExecution?> GetExecutionFromDbAsync(Guid decisionId)
    {
        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TadssDbContext>();
        return await dbContext.DecisionExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.DecisionId == decisionId);
    }

    #endregion
}
