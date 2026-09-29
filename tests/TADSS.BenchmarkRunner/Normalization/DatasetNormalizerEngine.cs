using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.BenchmarkRunner.Normalization;

/// <summary>
/// Động cơ chuẩn hóa dữ liệu nghiên cứu (Dataset Normalizer Engine — STEP 6):
/// Thực hiện phép nối 1:1 (Client RawMeasurementRecord ⋈ Server DecisionExecution) qua khóa DecisionId
/// và chiếu (project) sang 3 bộ dữ liệu chuẩn hóa đóng băng (23 / 19 / 31 cột) theo DATASET_SCHEMA_SPECIFICATION_V1.md.
/// </summary>
public static class DatasetNormalizerEngine
{
    private static readonly HashSet<string> ResilienceScenarioIds = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ExperimentScenarioId.R0),
        nameof(ExperimentScenarioId.R1),
        nameof(ExperimentScenarioId.R2),
        nameof(ExperimentScenarioId.R3),
        nameof(ExperimentScenarioId.D0),
        nameof(ExperimentScenarioId.D1),
        nameof(ExperimentScenarioId.D2),
        nameof(ExperimentScenarioId.E1),
        nameof(ExperimentScenarioId.E2),
        nameof(ExperimentScenarioId.E3),
        nameof(ExperimentScenarioId.E4)
    };

    private static readonly HashSet<string> LatencyCachingScenarioIds = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ExperimentScenarioId.G0),
        nameof(ExperimentScenarioId.G1),
        nameof(ExperimentScenarioId.G2),
        nameof(ExperimentScenarioId.C0),
        nameof(ExperimentScenarioId.C1),
        nameof(ExperimentScenarioId.C2),
        nameof(ExperimentScenarioId.C3),
        nameof(ExperimentScenarioId.E0)
    };

    public static bool IsResilienceScenario(string scenarioId) =>
        ResilienceScenarioIds.Contains(scenarioId);

    public static bool IsLatencyCachingScenario(string scenarioId) =>
        LatencyCachingScenarioIds.Contains(scenarioId);

    /// <summary>
    /// Thực hiện phép nối 1:1 (Bijective Inner Join) giữa M_valid và E_selected cho một lượt chạy.
    /// </summary>
    public static IReadOnlyList<JoinedExperimentRecord> JoinRunRecords(AuditHarvestResult harvestResult)
    {
        ArgumentNullException.ThrowIfNull(harvestResult);

        var auditByDecisionId = new Dictionary<Guid, DecisionExecution>(harvestResult.SelectedAuditExecutions.Count);
        foreach (var execution in harvestResult.SelectedAuditExecutions)
        {
            auditByDecisionId[execution.DecisionId] = execution;
        }

        var joined = new List<JoinedExperimentRecord>(harvestResult.SelectedAuditExecutions.Count);
        foreach (var clientRecord in harvestResult.MeasurementRecords.OrderBy(r => r.RequestIndex))
        {
            if (clientRecord.DecisionId == Guid.Empty)
            {
                continue;
            }

            if (auditByDecisionId.TryGetValue(clientRecord.DecisionId, out var auditExecution))
            {
                joined.Add(new JoinedExperimentRecord
                {
                    Metadata = harvestResult.Metadata,
                    Summary = harvestResult.Summary,
                    ClientRecord = clientRecord,
                    AuditExecution = auditExecution
                });
            }
        }

        return joined;
    }

    public static IReadOnlyList<JoinedExperimentRecord> JoinAllRuns(IEnumerable<AuditHarvestResult> harvestResults)
    {
        ArgumentNullException.ThrowIfNull(harvestResults);

        var allJoined = new List<JoinedExperimentRecord>();
        foreach (var runResult in harvestResults)
        {
            allJoined.AddRange(JoinRunRecords(runResult));
        }

        return allJoined;
    }

    /// <summary>
    /// Chiếu tập bản ghi đã nối 1:1 sang Schema 2.1 (resilience_evaluation_dataset — 23 cột)
    /// và Schema 2.2 (latency_caching_dataset — 19 cột).
    /// </summary>
    public static (IReadOnlyList<ResilienceEvaluationDatasetRow> ResilienceRows, IReadOnlyList<LatencyCachingDatasetRow> LatencyCachingRows)
        ProjectResilienceAndCachingDatasets(IEnumerable<JoinedExperimentRecord> joinedRecords)
    {
        ArgumentNullException.ThrowIfNull(joinedRecords);

        var resilienceRows = new List<ResilienceEvaluationDatasetRow>();
        var cachingRows = new List<LatencyCachingDatasetRow>();

        foreach (var item in joinedRecords)
        {
            string scenarioId = item.Metadata.Scenario;

            if (IsResilienceScenario(scenarioId))
            {
                resilienceRows.Add(MapToResilienceRow(item));
            }
            else if (IsLatencyCachingScenario(scenarioId))
            {
                cachingRows.Add(MapToLatencyCachingRow(item));
            }
        }

        return (resilienceRows, cachingRows);
    }

    public static ResilienceEvaluationDatasetRow MapToResilienceRow(JoinedExperimentRecord joined)
    {
        ArgumentNullException.ThrowIfNull(joined);

        var meta = joined.Metadata;
        var r = joined.ClientRecord;
        var e = joined.AuditExecution;
        double roundedElapsedMs = Math.Round(r.ClientElapsedMs, 3);

        return new ResilienceEvaluationDatasetRow
        {
            SchemaVersion = BenchmarkDefaults.Normalization.SchemaVersion,
            RunId = meta.RunId,
            ExperimentLayer = meta.ExperimentLayer,
            ScenarioId = meta.Scenario,
            ConcurrencyLevel = meta.Concurrency,
            SampleIndex = r.RequestIndex,
            DecisionId = e.DecisionId,
            ReservationId = e.ReservationId,
            StrategyName = e.StrategyName,
            StrategyVersion = e.StrategyVersion,
            ExecutionType = e.ExecutionType,
            DecisionStatus = r.DecisionStatus,
            FallbackUsed = e.FallbackUsed,
            FailureReason = e.FailureReason,
            NeedApproval = e.NeedApproval,
            CacheHit = e.CacheHit,
            HttpStatusCode = r.HttpStatusCode,
            ClientElapsedMs = roundedElapsedMs,
            ServerExecutionTimeMs = e.ExecutionTimeMs,
            WithinConfiguredOuterSla = roundedElapsedMs <= meta.OuterTimeoutMs,
            CircuitSetupMode = meta.CircuitStateSetup,
            IsEligibleForDcr = r.IsEligibleForDcr,
            CreatedAtUtc = FormatIso8601Utc(e.CreatedAt)
        };
    }

    public static LatencyCachingDatasetRow MapToLatencyCachingRow(JoinedExperimentRecord joined)
    {
        ArgumentNullException.ThrowIfNull(joined);

        var meta = joined.Metadata;
        var summary = joined.Summary;
        var r = joined.ClientRecord;
        var e = joined.AuditExecution;
        double roundedElapsedMs = Math.Round(r.ClientElapsedMs, 3);
        double latencyDeltaMs = Math.Round(Math.Max(0.0, r.ClientElapsedMs - e.ExecutionTimeMs), 3);

        return new LatencyCachingDatasetRow
        {
            SchemaVersion = BenchmarkDefaults.Normalization.SchemaVersion,
            RunId = meta.RunId,
            ExperimentLayer = meta.ExperimentLayer,
            ScenarioId = meta.Scenario,
            ConcurrencyLevel = meta.Concurrency,
            SampleIndex = r.RequestIndex,
            DecisionId = e.DecisionId,
            ReservationId = e.ReservationId,
            CacheStateMode = meta.CacheState,
            CacheHit = e.CacheHit,
            ExecutionType = e.ExecutionType,
            RequestHash = e.RequestHash,
            IdempotencyKey = e.IdempotencyKey ?? string.Empty,
            ClientElapsedMs = roundedElapsedMs,
            ServerExecutionTimeMs = e.ExecutionTimeMs,
            ClientServerLatencyDeltaMs = latencyDeltaMs,
            RunCacheHitRatioPercent = Math.Round(summary.CacheHitRatioPercent, 2),
            WithinSla150Ms = roundedElapsedMs <= BenchmarkDefaults.Normalization.Sla150ThresholdMs,
            CreatedAtUtc = FormatIso8601Utc(e.CreatedAt)
        };
    }

    /// <summary>
    /// Chiếu các quan sát vòng đời quản trị sang Schema 2.3 (governance_audit_dataset — 31 cột).
    /// </summary>
    public static IReadOnlyList<GovernanceAuditDatasetRow> ProjectGovernanceAuditDataset(
        IEnumerable<GovernanceLifecycleObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var rows = new List<GovernanceAuditDatasetRow>();
        foreach (var obs in observations)
        {
            rows.Add(MapToGovernanceAuditRow(obs));
        }

        return rows;
    }

    public static GovernanceAuditDatasetRow MapToGovernanceAuditRow(GovernanceLifecycleObservation obs)
    {
        ArgumentNullException.ThrowIfNull(obs);

        var t0 = obs.SnapshotT0;
        var t1 = obs.SnapshotT1;
        string payloadHashT0 = ComputeSha256Hex(t0.ResponsePayload);
        string payloadHashT1 = ComputeSha256Hex(t1.ResponsePayload);
        bool immutableVerified = VerifyNineMachineFieldsPlusNeedApprovalEqual(t0, t1);

        return new GovernanceAuditDatasetRow
        {
            SchemaVersion = BenchmarkDefaults.Normalization.SchemaVersion,
            GovernanceCaseId = obs.GovernanceCaseId,
            GovernanceScenarioGroup = obs.GovernanceScenarioGroup,
            GovernanceCaseType = obs.GovernanceCaseType,
            DecisionId = t1.DecisionId,
            ReservationId = t1.ReservationId,
            StrategyNameT0 = t0.StrategyName,
            StrategyNameT1 = t1.StrategyName,
            StrategyVersionT0 = t0.StrategyVersion,
            StrategyVersionT1 = t1.StrategyVersion,
            ExecutionTypeT0 = t0.ExecutionType,
            ExecutionTypeT1 = t1.ExecutionType,
            ExecutionTimeMsT0 = t0.ExecutionTimeMs,
            ExecutionTimeMsT1 = t1.ExecutionTimeMs,
            RequestHashT0 = t0.RequestHash,
            RequestHashT1 = t1.RequestHash,
            ResponsePayloadSha256T0 = payloadHashT0,
            ResponsePayloadSha256T1 = payloadHashT1,
            CreatedAtUtcT0 = FormatIso8601Utc(t0.CreatedAt),
            CreatedAtUtcT1 = FormatIso8601Utc(t1.CreatedAt),
            NeedApprovalT0 = t0.NeedApproval,
            NeedApprovalT1 = t1.NeedApproval,
            WasInPendingQueueT0 = obs.WasInPendingQueueT0,
            InPendingQueueT1 = obs.InPendingQueueT1,
            OverrideHttpStatus = obs.OverrideHttpStatus,
            IsOverriddenT0 = t0.IsOverridden,
            IsOverriddenT1 = t1.IsOverridden,
            OverriddenByT1 = t1.OverriddenBy,
            OverrideReasonT1 = t1.OverrideReason,
            VersionTransition = $"{t0.Version}->{t1.Version}",
            MachineSnapshotImmutableVerified = immutableVerified
        };
    }

    /// <summary>
    /// Kiểm tra chính xác 9 thuộc tính Machine Snapshot (DecisionId, ReservationId, StrategyName, StrategyVersion,
    /// ExecutionType, ExecutionTimeMs, RequestHash, ResponsePayload, CreatedAt) + trường trạng thái quyết định bất biến NeedApproval.
    /// </summary>
    public static bool VerifyNineMachineFieldsPlusNeedApprovalEqual(DecisionExecution t0, DecisionExecution t1)
    {
        ArgumentNullException.ThrowIfNull(t0);
        ArgumentNullException.ThrowIfNull(t1);

        return t0.DecisionId == t1.DecisionId &&
               t0.ReservationId == t1.ReservationId &&
               string.Equals(t0.StrategyName, t1.StrategyName, StringComparison.Ordinal) &&
               string.Equals(t0.StrategyVersion, t1.StrategyVersion, StringComparison.Ordinal) &&
               string.Equals(t0.ExecutionType, t1.ExecutionType, StringComparison.Ordinal) &&
               t0.ExecutionTimeMs == t1.ExecutionTimeMs &&
               string.Equals(t0.RequestHash, t1.RequestHash, StringComparison.Ordinal) &&
               string.Equals(t0.ResponsePayload, t1.ResponsePayload, StringComparison.Ordinal) &&
               t0.CreatedAt == t1.CreatedAt &&
               t0.NeedApproval == t1.NeedApproval;
    }

    public static string ComputeSha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FormatIso8601Utc(DateTime utcDateTime) =>
        utcDateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
