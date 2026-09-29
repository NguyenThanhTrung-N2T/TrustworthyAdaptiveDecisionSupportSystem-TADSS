using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Governance;
using TADSS.BenchmarkRunner.Harness;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Data;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.BenchmarkRunner.Normalization;

/// <summary>
/// Thực thi tổ hợp kịch bản kiểm định vòng đời Quản trị Kết quả Quyết định (Controlled Governance Lifecycle Suite — STEP 8):
/// - G-A (POSITIVE_CONTROL): Fallback -> PendingQueue (t0) -> Decision-Outcome Override (HTTP 200) -> Removed from PendingQueue (t1).
/// - G-B (POSITIVE_CONTROL): Automated High-Value NEED_APPROVAL -> PendingQueue (t0) -> Decision-Outcome Override (HTTP 200) -> Removed (t1).
/// - G-C1 (NEGATIVE_CONTROL): Duplicate Override Attempt on Already-Overridden Decision -> Rejected (HTTP 400), State Preserved (2->2).
/// - G-C2 (NEGATIVE_CONTROL): Override Attempt on Non-Approvable Automated Decision (NeedApproval=false) -> Rejected (HTTP 400), State Preserved (1->1).
/// </summary>
public static class GovernanceLifecycleHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task<IReadOnlyList<GovernanceLifecycleObservation>> ExecuteSuiteAsync(
        BenchmarkSuiteConfig suiteConfig,
        int positiveFallbackCases = BenchmarkDefaults.Normalization.DefaultGovernancePositiveFallbackCases,
        int positiveAutomatedReviewCases = BenchmarkDefaults.Normalization.DefaultGovernancePositiveAutomatedReviewCases,
        int negativeDuplicateCases = BenchmarkDefaults.Normalization.DefaultGovernanceNegativeDuplicateCases,
        int negativeNonApprovableCases = BenchmarkDefaults.Normalization.DefaultGovernanceNegativeNonApprovableCases,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suiteConfig);

        var observations = new List<GovernanceLifecycleObservation>(
            positiveFallbackCases + positiveAutomatedReviewCases + negativeDuplicateCases + negativeNonApprovableCases);

        var duplicateControlObservations = new List<GovernanceLifecycleObservation>(negativeDuplicateCases);

        // 1. G-A (Fallback Override Positive Control) & G-C1 (Duplicate Override Negative Control)
        await using (var fallbackHost = new ScenarioHostController(ExperimentScenarioId.R2, concurrency: 1, suiteConfig))
        {
            using var client = fallbackHost.CreateBenchmarkClient(out _, totalRequests: positiveFallbackCases, warmupRequests: 2);
            await fallbackHost.ExecuteWarmupAndLockStateAsync(client, warmupRequests: 2);

            for (int i = 1; i <= positiveFallbackCases; i++)
            {
                var payload = CreateFallbackOrStandardPayload(index: 1000 + i);
                string idempotencyKey = $"GOV-GA-REQ-{i:D4}";
                Guid decisionId = await EvaluateDecisionAsync(client, payload, idempotencyKey, cancellationToken);

                var snapshotT0 = await LoadExecutionSnapshotAsync(fallbackHost, decisionId, cancellationToken);
                bool inQueueT0 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                string targetOutcome = (i % 2 == 1) ? "APPROVED" : "REJECTED";
                var overrideReq = new OverrideDecisionRequestDto
                {
                    Decision = new OverrideTargetPayloadDto { Status = targetOutcome },
                    OverriddenBy = BenchmarkDefaults.Normalization.DefaultOperatorId,
                    OverrideReason = $"Governance decision-outcome override for fallback case G-A-{i:D3} to {targetOutcome}."
                };

                int statusCode = await SendOverrideRequestAsync(client, decisionId, overrideReq, cancellationToken);
                var snapshotT1 = await LoadExecutionSnapshotAsync(fallbackHost, decisionId, cancellationToken);
                bool inQueueT1 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                observations.Add(new GovernanceLifecycleObservation
                {
                    GovernanceCaseId = $"G-A-{i:D3}",
                    GovernanceScenarioGroup = "G-A_FALLBACK_OVERRIDE",
                    GovernanceCaseType = "POSITIVE_CONTROL",
                    SnapshotT0 = snapshotT0,
                    SnapshotT1 = snapshotT1,
                    WasInPendingQueueT0 = inQueueT0,
                    InPendingQueueT1 = inQueueT1,
                    OverrideHttpStatus = statusCode
                });

                if (i <= negativeDuplicateCases)
                {
                    var dupSnapshotT0 = AuditLedgerHarvester.CloneExecution(snapshotT1);
                    bool dupInQueueT0 = inQueueT1;

                    var duplicateReq = new OverrideDecisionRequestDto
                    {
                        Decision = new OverrideTargetPayloadDto { Status = "REJECTED" },
                        OverriddenBy = "unauthorized-second-override@smashcourt.vn",
                        OverrideReason = $"Duplicate override negative control attempt G-C1-{i:D3}."
                    };

                    int dupStatusCode = await SendOverrideRequestAsync(client, decisionId, duplicateReq, cancellationToken);
                    var dupSnapshotT1 = await LoadExecutionSnapshotAsync(fallbackHost, decisionId, cancellationToken);
                    bool dupInQueueT1 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                    duplicateControlObservations.Add(new GovernanceLifecycleObservation
                    {
                        GovernanceCaseId = $"G-C1-{i:D3}",
                        GovernanceScenarioGroup = "G-C1_DUPLICATE_OVERRIDE_GUARD",
                        GovernanceCaseType = "NEGATIVE_CONTROL",
                        SnapshotT0 = dupSnapshotT0,
                        SnapshotT1 = dupSnapshotT1,
                        WasInPendingQueueT0 = dupInQueueT0,
                        InPendingQueueT1 = dupInQueueT1,
                        OverrideHttpStatus = dupStatusCode
                    });
                }
            }
        }

        // 2. G-B (Automated Review Override Positive Control) & G-C2 (Non-Approvable Automated Decision Negative Control)
        await using (var normalHost = new ScenarioHostController(ExperimentScenarioId.R0, concurrency: 1, suiteConfig))
        {
            using var client = normalHost.CreateBenchmarkClient(
                out _,
                totalRequests: positiveAutomatedReviewCases + negativeNonApprovableCases,
                warmupRequests: 2);
            await normalHost.ExecuteWarmupAndLockStateAsync(client, warmupRequests: 2);

            for (int i = 1; i <= positiveAutomatedReviewCases; i++)
            {
                var highValuePayload = CreateHighValueReviewPayload(index: 2000 + i);
                string idempotencyKey = $"GOV-GB-REQ-{i:D4}";
                Guid decisionId = await EvaluateDecisionAsync(client, highValuePayload, idempotencyKey, cancellationToken);

                var snapshotT0 = await LoadExecutionSnapshotAsync(normalHost, decisionId, cancellationToken);
                bool inQueueT0 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                string targetOutcome = (i % 2 == 1) ? "APPROVED" : "REJECTED";
                var overrideReq = new OverrideDecisionRequestDto
                {
                    Decision = new OverrideTargetPayloadDto { Status = targetOutcome },
                    OverriddenBy = BenchmarkDefaults.Normalization.DefaultOperatorId,
                    OverrideReason = $"Governance decision-outcome override for high-value review case G-B-{i:D3} to {targetOutcome}."
                };

                int statusCode = await SendOverrideRequestAsync(client, decisionId, overrideReq, cancellationToken);
                var snapshotT1 = await LoadExecutionSnapshotAsync(normalHost, decisionId, cancellationToken);
                bool inQueueT1 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                observations.Add(new GovernanceLifecycleObservation
                {
                    GovernanceCaseId = $"G-B-{i:D3}",
                    GovernanceScenarioGroup = "G-B_AUTOMATED_REVIEW_OVERRIDE",
                    GovernanceCaseType = "POSITIVE_CONTROL",
                    SnapshotT0 = snapshotT0,
                    SnapshotT1 = snapshotT1,
                    WasInPendingQueueT0 = inQueueT0,
                    InPendingQueueT1 = inQueueT1,
                    OverrideHttpStatus = statusCode
                });
            }

            // Append G-C1 observations before G-C2 so ordering is G-A -> G-B -> G-C1 -> G-C2
            observations.AddRange(duplicateControlObservations);

            for (int i = 1; i <= negativeNonApprovableCases; i++)
            {
                var approvedPayload = CreateFallbackOrStandardPayload(index: 3000 + i);
                string idempotencyKey = $"GOV-GC2-REQ-{i:D4}";
                Guid decisionId = await EvaluateDecisionAsync(client, approvedPayload, idempotencyKey, cancellationToken);

                var snapshotT0 = await LoadExecutionSnapshotAsync(normalHost, decisionId, cancellationToken);
                bool inQueueT0 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                var invalidOverrideReq = new OverrideDecisionRequestDto
                {
                    Decision = new OverrideTargetPayloadDto { Status = "REJECTED" },
                    OverriddenBy = BenchmarkDefaults.Normalization.DefaultOperatorId,
                    OverrideReason = $"Invalid override attempt on auto-approved decision G-C2-{i:D3}."
                };

                int statusCode = await SendOverrideRequestAsync(client, decisionId, invalidOverrideReq, cancellationToken);
                var snapshotT1 = await LoadExecutionSnapshotAsync(normalHost, decisionId, cancellationToken);
                bool inQueueT1 = await IsDecisionInPendingQueueAsync(client, decisionId, cancellationToken);

                observations.Add(new GovernanceLifecycleObservation
                {
                    GovernanceCaseId = $"G-C2-{i:D3}",
                    GovernanceScenarioGroup = "G-C2_NON_APPROVABLE_GUARD",
                    GovernanceCaseType = "NEGATIVE_CONTROL",
                    SnapshotT0 = snapshotT0,
                    SnapshotT1 = snapshotT1,
                    WasInPendingQueueT0 = inQueueT0,
                    InPendingQueueT1 = inQueueT1,
                    OverrideHttpStatus = statusCode
                });
            }
        }

        return observations;
    }

    private static async Task<Guid> EvaluateDecisionAsync(
        HttpClient client,
        DecisionRequestDto payload,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/decisions")
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("X-Idempotency-Key", idempotencyKey);

        using var response = await client.SendAsync(req, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DecisionResultDto>(JsonOptions, cancellationToken)
                     ?? throw new InvalidOperationException("Decision evaluation response returned null DTO.");

        return result.DecisionId;
    }

    private static async Task<int> SendOverrideRequestAsync(
        HttpClient client,
        Guid decisionId,
        OverrideDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/decisions/{decisionId}/override",
            request,
            cancellationToken);

        return (int)response.StatusCode;
    }

    private static async Task<bool> IsDecisionInPendingQueueAsync(
        HttpClient client,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            "/api/v1/decisions/pending-approvals?page=1&pageSize=100",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var paged = await response.Content.ReadFromJsonAsync<PagedResult<PendingApprovalItemDto>>(
            JsonOptions,
            cancellationToken);

        return paged?.Items.Any(item => item.DecisionId == decisionId) ?? false;
    }

    private static async Task<DecisionExecution> LoadExecutionSnapshotAsync(
        ScenarioHostController hostController,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        using var scope = hostController.CreateServiceScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TadssDbContext>();

        var entity = await dbContext.DecisionExecutions
            .AsNoTracking()
            .SingleAsync(e => e.DecisionId == decisionId, cancellationToken);

        return AuditLedgerHarvester.CloneExecution(entity);
    }

    private static DecisionRequestDto CreateFallbackOrStandardPayload(int index)
    {
        var fixedStart = BenchmarkDefaults.PayloadSeed.FixedStartTimeUtc.AddHours(index % 48);
        return new DecisionRequestDto
        {
            ReservationId = CreateDeterministicGovernanceGuid("GOV-STD", index),
            CustomerId = BenchmarkDefaults.PayloadSeed.CanonicalCustomerId,
            CustomerTier = BenchmarkDefaults.PayloadSeed.CustomerTier,
            CourtId = BenchmarkDefaults.PayloadSeed.CanonicalCourtId,
            BranchId = BenchmarkDefaults.PayloadSeed.CanonicalBranchId,
            CourtTypeId = null,
            StartTime = fixedStart,
            EndTime = fixedStart.AddHours(BenchmarkDefaults.PayloadSeed.DurationHours),
            TotalPrice = BenchmarkDefaults.PayloadSeed.TotalPrice + index,
            DepositAmount = BenchmarkDefaults.PayloadSeed.DepositAmount + index,
            PaymentStatus = BenchmarkDefaults.PayloadSeed.PaymentStatus,
            BookingType = BenchmarkDefaults.PayloadSeed.BookingType,
            Priority = BenchmarkDefaults.PayloadSeed.Priority,
            RequestedAt = fixedStart.AddHours(-BenchmarkDefaults.PayloadSeed.RequestedHoursBeforeStart)
        };
    }

    private static DecisionRequestDto CreateHighValueReviewPayload(int index)
    {
        var fixedStart = BenchmarkDefaults.PayloadSeed.FixedStartTimeUtc.AddHours(index % 48);
        return new DecisionRequestDto
        {
            ReservationId = CreateDeterministicGovernanceGuid("GOV-HV", index),
            CustomerId = BenchmarkDefaults.PayloadSeed.CanonicalCustomerId,
            CustomerTier = BenchmarkDefaults.Normalization.HighValueReviewCustomerTier,
            CourtId = BenchmarkDefaults.PayloadSeed.CanonicalCourtId,
            BranchId = BenchmarkDefaults.PayloadSeed.CanonicalBranchId,
            CourtTypeId = null,
            StartTime = fixedStart,
            EndTime = fixedStart.AddHours(BenchmarkDefaults.PayloadSeed.DurationHours),
            TotalPrice = BenchmarkDefaults.Normalization.HighValueReviewTotalPrice + (index * 100m),
            DepositAmount = BenchmarkDefaults.Normalization.HighValueReviewDepositAmount,
            PaymentStatus = BenchmarkDefaults.Normalization.HighValueReviewPaymentStatus,
            BookingType = BenchmarkDefaults.PayloadSeed.BookingType,
            Priority = BenchmarkDefaults.PayloadSeed.Priority,
            RequestedAt = fixedStart.AddHours(-BenchmarkDefaults.PayloadSeed.RequestedHoursBeforeStart)
        };
    }

    private static Guid CreateDeterministicGovernanceGuid(string tag, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..4], tag.GetHashCode(StringComparison.Ordinal));
        BitConverter.TryWriteBytes(bytes[4..8], index);
        BitConverter.TryWriteBytes(bytes[8..12], 0x474F5639);
        BitConverter.TryWriteBytes(bytes[12..16], BenchmarkDefaults.PayloadSeed.DeterministicGuidTag);
        return new Guid(bytes);
    }
}
