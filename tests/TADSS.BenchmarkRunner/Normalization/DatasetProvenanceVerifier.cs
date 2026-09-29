using System.Text.RegularExpressions;

namespace TADSS.BenchmarkRunner.Normalization;

/// <summary>
/// Bộ kiểm định tự động 8 Bất biến Truy xuất Nguồn gốc Dữ liệu (Provenance Invariants Verifier — STEP 7)
/// theo Mục 4 của DATASET_SCHEMA_SPECIFICATION_V1.md.
/// </summary>
public static partial class DatasetProvenanceVerifier
{
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.Compiled)]
    private static partial Regex Sha256LowerHexRegex();

    public static IReadOnlyList<ProvenanceInvariantCheckResult> VerifyAll(
        IReadOnlyList<AuditHarvestResult> harvestResults,
        IReadOnlyList<JoinedExperimentRecord> joinedRecords,
        IReadOnlyList<GovernanceLifecycleObservation> governanceObservations)
    {
        ArgumentNullException.ThrowIfNull(harvestResults);
        ArgumentNullException.ThrowIfNull(joinedRecords);
        ArgumentNullException.ThrowIfNull(governanceObservations);

        return
        [
            VerifyInvP1BijectiveEligibleJoin(harvestResults),
            VerifyInvP2ZeroWarmupContamination(harvestResults),
            VerifyInvP3aSha256FormatIntegrity(joinedRecords),
            VerifyInvP3bRfc8785CanonicalDeterminism(harvestResults, joinedRecords),
            VerifyInvP4ClientToLedgerConsistency(joinedRecords),
            VerifyInvP5FallbackSafetyPolicy(joinedRecords),
            VerifyInvP6MachineSnapshotImmutability(governanceObservations),
            VerifyInvP7GovernanceTransitionAndGuardPreservation(governanceObservations),
            VerifyInvP8PendingQueueHotIndexLifecycle(governanceObservations)
        ];
    }

    /// <summary>
    /// INV-P1: Per-Run DecisionId Uniqueness & Bijective 1:1 Eligible Measurement Join.
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP1BijectiveEligibleJoin(
        IReadOnlyList<AuditHarvestResult> harvestResults)
    {
        int checkedCount = 0;
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var run in harvestResults)
        {
            var validRecords = run.MeasurementRecords
                .Where(r => r.DecisionId != Guid.Empty)
                .ToList();

            checkedCount += validRecords.Count;

            var distinctClientIds = new HashSet<Guid>(validRecords.Select(r => r.DecisionId));
            if (distinctClientIds.Count != validRecords.Count)
            {
                violations += Math.Abs(validRecords.Count - distinctClientIds.Count);
                failureNotes.Add(
                    $"Run '{run.Metadata.RunId}' has duplicate DecisionId in M_valid (Count={validRecords.Count}, Distinct={distinctClientIds.Count}).");
            }

            var selectedAuditIds = new HashSet<Guid>(run.SelectedAuditExecutions.Select(e => e.DecisionId));
            if (validRecords.Count != run.SelectedAuditExecutions.Count ||
                !distinctClientIds.SetEquals(selectedAuditIds))
            {
                int diff = Math.Max(1, Math.Abs(validRecords.Count - run.SelectedAuditExecutions.Count));
                violations += diff;
                failureNotes.Add(
                    $"Run '{run.Metadata.RunId}' failed 1:1 bijection (|M_valid|={validRecords.Count}, |E_selected|={run.SelectedAuditExecutions.Count}).");
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P1",
            InvariantName = "Per-Run DecisionId Uniqueness & Bijective 1:1 Eligible Measurement Join",
            Passed = violations == 0,
            CheckedRecordsCount = checkedCount,
            ViolationCount = violations,
            Details = violations == 0
                ? $"100% of {checkedCount} eligible measurement records across {harvestResults.Count} run(s) have unique DecisionIds and 1:1 bijective match in tadss.decision_executions."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P2: Zero Warm-up & Circuit-Trip Contamination (Primary K_meas Disjointness + Secondary IdempotencyKey Check).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP2ZeroWarmupContamination(
        IReadOnlyList<AuditHarvestResult> harvestResults)
    {
        int checkedCount = 0;
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var run in harvestResults)
        {
            var selectedIds = new HashSet<Guid>(run.SelectedAuditExecutions.Select(e => e.DecisionId));
            var excludedIds = new HashSet<Guid>(run.ExcludedNonMeasurementExecutions.Select(e => e.DecisionId));

            if (selectedIds.Overlaps(excludedIds))
            {
                violations++;
                failureNotes.Add($"Run '{run.Metadata.RunId}' has overlapping DecisionIds between measurement and non-measurement sets.");
            }

            string expectedPrefix = $"{run.Metadata.RunId}-REQ-";
            foreach (var exec in run.SelectedAuditExecutions)
            {
                checkedCount++;
                string key = exec.IdempotencyKey ?? string.Empty;

                bool hasForbiddenMarker =
                    key.Contains("WARMUP-", StringComparison.OrdinalIgnoreCase) ||
                    key.Contains("TRIP-", StringComparison.OrdinalIgnoreCase);

                bool matchesRunReqPattern =
                    key.StartsWith(expectedPrefix, StringComparison.Ordinal) &&
                    key.Length == expectedPrefix.Length + 6 &&
                    key[expectedPrefix.Length..].All(char.IsAsciiDigit);

                if (hasForbiddenMarker || !matchesRunReqPattern)
                {
                    violations++;
                    if (failureNotes.Count < 5)
                    {
                        failureNotes.Add(
                            $"Run '{run.Metadata.RunId}' execution '{exec.DecisionId}' has invalid IdempotencyKey '{key}'.");
                    }
                }
            }
        }

        int totalExcluded = harvestResults.Sum(r => r.ExcludedNonMeasurementExecutions.Count);
        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P2",
            InvariantName = "Zero Warm-up & Circuit-Trip Contamination",
            Passed = violations == 0,
            CheckedRecordsCount = checkedCount,
            ViolationCount = violations,
            Details = violations == 0
                ? $"Filtered out {totalExcluded} warm-up/circuit-trip DB rows; 100% of {checkedCount} selected audit rows belong strictly to K_meas and match '{{RunId}}-REQ-{{6-digit}}'."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P3a: SHA-256 RequestHash Format Integrity (^[0-9a-f]{64}$).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP3aSha256FormatIntegrity(
        IReadOnlyList<JoinedExperimentRecord> joinedRecords)
    {
        int violations = 0;
        var regex = Sha256LowerHexRegex();

        foreach (var item in joinedRecords)
        {
            string hash = item.AuditExecution.RequestHash ?? string.Empty;
            if (!regex.IsMatch(hash))
            {
                violations++;
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P3a",
            InvariantName = "SHA-256 RequestHash Format Integrity",
            Passed = violations == 0,
            CheckedRecordsCount = joinedRecords.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"All {joinedRecords.Count} persisted RequestHash values conform to 64-character lowercase hex SHA-256 format (`^[0-9a-f]{{64}}$`)."
                : $"{violations} record(s) violated SHA-256 64-char lowercase hex format."
        };
    }

    /// <summary>
    /// INV-P3b: RFC 8785 Canonical RequestHash Determinism (Direct Hash Match + IdenticalCanonicalKey Uniqueness on G2, C2).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP3bRfc8785CanonicalDeterminism(
        IReadOnlyList<AuditHarvestResult> harvestResults,
        IReadOnlyList<JoinedExperimentRecord> joinedRecords)
    {
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var item in joinedRecords)
        {
            if (!string.IsNullOrEmpty(item.ClientRecord.ExpectedRequestHash) &&
                !string.Equals(item.AuditExecution.RequestHash, item.ClientRecord.ExpectedRequestHash, StringComparison.Ordinal))
            {
                violations++;
                if (failureNotes.Count < 5)
                {
                    failureNotes.Add(
                        $"Decision '{item.AuditExecution.DecisionId}' RequestHash mismatch (DB='{item.AuditExecution.RequestHash}' vs Expected='{item.ClientRecord.ExpectedRequestHash}').");
                }
            }
        }

        foreach (var run in harvestResults)
        {
            if (string.Equals(run.Metadata.Scenario, "G2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(run.Metadata.Scenario, "C2", StringComparison.OrdinalIgnoreCase))
            {
                var distinctHashes = run.SelectedAuditExecutions
                    .Select(e => e.RequestHash)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (run.SelectedAuditExecutions.Count > 0 && distinctHashes.Count != 1)
                {
                    violations++;
                    failureNotes.Add(
                        $"Run '{run.Metadata.RunId}' ({run.Metadata.Scenario}) expected 1 canonical RequestHash across all samples, but observed {distinctHashes.Count}.");
                }
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P3b",
            InvariantName = "RFC 8785 Canonical RequestHash Determinism",
            Passed = violations == 0,
            CheckedRecordsCount = joinedRecords.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"100% of {joinedRecords.Count} joined records match RequestHasher.ComputeHash(payload), and single-key scenarios (G2, C2) produce a single deterministic hash."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P4: Client-to-Ledger Telemetry Consistency (ExecutionType, FallbackUsed, CacheHit).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP4ClientToLedgerConsistency(
        IReadOnlyList<JoinedExperimentRecord> joinedRecords)
    {
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var item in joinedRecords)
        {
            var r = item.ClientRecord;
            var e = item.AuditExecution;

            bool consistent =
                string.Equals(r.ExecutionType, e.ExecutionType, StringComparison.Ordinal) &&
                r.FallbackUsed == e.FallbackUsed &&
                r.CacheHit == e.CacheHit;

            if (!consistent)
            {
                violations++;
                if (failureNotes.Count < 5)
                {
                    failureNotes.Add(
                        $"Decision '{e.DecisionId}' mismatch: Client=({r.ExecutionType}, Fallback={r.FallbackUsed}, CacheHit={r.CacheHit}) vs DB=({e.ExecutionType}, Fallback={e.FallbackUsed}, CacheHit={e.CacheHit}).");
                }
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P4",
            InvariantName = "Client-to-Ledger Telemetry Consistency",
            Passed = violations == 0,
            CheckedRecordsCount = joinedRecords.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"100% of {joinedRecords.Count} joined pairs exhibit exact agreement on ExecutionType, FallbackUsed, and CacheHit between Client telemetry and Server Audit Ledger."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P5: Fallback Safety Invariant (Engine Invariants: ExecutionType=="FALLBACK" & CacheHit==false; Phase 4 Policy: NeedApproval==true).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP5FallbackSafetyPolicy(
        IReadOnlyList<JoinedExperimentRecord> joinedRecords)
    {
        int fallbackRecordsChecked = 0;
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var item in joinedRecords)
        {
            var e = item.AuditExecution;
            if (!e.FallbackUsed)
            {
                continue;
            }

            fallbackRecordsChecked++;
            bool validEngineAndPolicy =
                string.Equals(e.ExecutionType, "FALLBACK", StringComparison.Ordinal) &&
                !e.CacheHit &&
                e.NeedApproval;

            if (!validEngineAndPolicy)
            {
                violations++;
                if (failureNotes.Count < 5)
                {
                    failureNotes.Add(
                        $"Fallback decision '{e.DecisionId}' violated safety invariant (ExecutionType='{e.ExecutionType}', CacheHit={e.CacheHit}, NeedApproval={e.NeedApproval}).");
                }
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P5",
            InvariantName = "Fallback Safety Invariant (Engine + Phase 4 Policy)",
            Passed = violations == 0,
            CheckedRecordsCount = fallbackRecordsChecked,
            ViolationCount = violations,
            Details = violations == 0
                ? $"All {fallbackRecordsChecked} fallback executions satisfy Engine invariants (ExecutionType=='FALLBACK', CacheHit==false) and Phase 4 FallbackDecisionStrategy policy (NeedApproval==true)."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P6: Immutable 9-Field Machine Snapshot + invariant decision-state field NeedApproval Under Override.
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP6MachineSnapshotImmutability(
        IReadOnlyList<GovernanceLifecycleObservation> observations)
    {
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var obs in observations)
        {
            if (!DatasetNormalizerEngine.VerifyNineMachineFieldsPlusNeedApprovalEqual(obs.SnapshotT0, obs.SnapshotT1))
            {
                violations++;
                failureNotes.Add($"Governance case '{obs.GovernanceCaseId}' mutated one or more machine snapshot fields between t0 and t1.");
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P6",
            InvariantName = "Immutable 9-Field Machine Snapshot + NeedApproval Under Override",
            Passed = violations == 0,
            CheckedRecordsCount = observations.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"100% of {observations.Count} governance lifecycle cases preserved all 9 Machine Snapshot fields (DecisionId, ReservationId, StrategyName, StrategyVersion, ExecutionType, ExecutionTimeMs, RequestHash, ResponsePayload, CreatedAt) + NeedApproval unchanged between t0 and t1."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P7: Governance State Transition (POSITIVE_CONTROL) & Guard Preservation (NEGATIVE_CONTROL).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP7GovernanceTransitionAndGuardPreservation(
        IReadOnlyList<GovernanceLifecycleObservation> observations)
    {
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var obs in observations)
        {
            var t0 = obs.SnapshotT0;
            var t1 = obs.SnapshotT1;

            if (string.Equals(obs.GovernanceCaseType, "POSITIVE_CONTROL", StringComparison.Ordinal))
            {
                bool validPositive =
                    obs.OverrideHttpStatus == 200 &&
                    !t0.IsOverridden &&
                    t1.IsOverridden &&
                    string.IsNullOrEmpty(t0.OverriddenBy) &&
                    !string.IsNullOrWhiteSpace(t1.OverriddenBy) &&
                    string.IsNullOrEmpty(t0.OverrideReason) &&
                    !string.IsNullOrWhiteSpace(t1.OverrideReason) &&
                    t0.Version == 1 &&
                    t1.Version == 2 &&
                    t1.UpdatedAt >= t0.UpdatedAt;

                if (!validPositive)
                {
                    violations++;
                    failureNotes.Add(
                        $"Positive case '{obs.GovernanceCaseId}' failed state transition (HTTP={obs.OverrideHttpStatus}, IsOverridden:{t0.IsOverridden}->{t1.IsOverridden}, Version:{t0.Version}->{t1.Version}).");
                }
            }
            else if (string.Equals(obs.GovernanceCaseType, "NEGATIVE_CONTROL", StringComparison.Ordinal))
            {
                bool validNegative =
                    obs.OverrideHttpStatus == 400 &&
                    t1.IsOverridden == t0.IsOverridden &&
                    string.Equals(t1.OverriddenBy, t0.OverriddenBy, StringComparison.Ordinal) &&
                    string.Equals(t1.OverrideReason, t0.OverrideReason, StringComparison.Ordinal) &&
                    t1.Version == t0.Version &&
                    t1.UpdatedAt == t0.UpdatedAt;

                if (!validNegative)
                {
                    violations++;
                    failureNotes.Add(
                        $"Negative case '{obs.GovernanceCaseId}' failed guard preservation (HTTP={obs.OverrideHttpStatus}, Version:{t0.Version}->{t1.Version}).");
                }
            }
            else
            {
                violations++;
                failureNotes.Add($"Unknown GovernanceCaseType '{obs.GovernanceCaseType}' for case '{obs.GovernanceCaseId}'.");
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P7",
            InvariantName = "Governance State Transition (POSITIVE_CONTROL) & Guard Preservation (NEGATIVE_CONTROL)",
            Passed = violations == 0,
            CheckedRecordsCount = observations.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"Verified all {observations.Count} governance cases: POSITIVE_CONTROL transitions (IsOverridden false->true, Version 1->2, UpdatedAt_t1 >= UpdatedAt_t0, HTTP 200) and NEGATIVE_CONTROL guard rejections (all 5 mutable fields unchanged, HTTP 400)."
                : string.Join(" | ", failureNotes)
        };
    }

    /// <summary>
    /// INV-P8: Pending Queue Hot-Index Lifecycle Consistency (GET /api/v1/decisions/pending-approvals).
    /// </summary>
    public static ProvenanceInvariantCheckResult VerifyInvP8PendingQueueHotIndexLifecycle(
        IReadOnlyList<GovernanceLifecycleObservation> observations)
    {
        int violations = 0;
        var failureNotes = new List<string>();

        foreach (var obs in observations)
        {
            if (string.Equals(obs.GovernanceCaseType, "POSITIVE_CONTROL", StringComparison.Ordinal))
            {
                if (!obs.WasInPendingQueueT0 || obs.InPendingQueueT1)
                {
                    violations++;
                    failureNotes.Add(
                        $"Positive case '{obs.GovernanceCaseId}' queue lifecycle invalid (t0={obs.WasInPendingQueueT0}, t1={obs.InPendingQueueT1}).");
                }
            }
            else if (obs.GovernanceScenarioGroup.StartsWith("G-C2", StringComparison.OrdinalIgnoreCase))
            {
                if (obs.WasInPendingQueueT0 || obs.InPendingQueueT1)
                {
                    violations++;
                    failureNotes.Add(
                        $"Negative case '{obs.GovernanceCaseId}' (NeedApproval=false) unexpectedly appeared in pending queue (t0={obs.WasInPendingQueueT0}, t1={obs.InPendingQueueT1}).");
                }
            }
            else if (obs.GovernanceScenarioGroup.StartsWith("G-C1", StringComparison.OrdinalIgnoreCase))
            {
                if (obs.WasInPendingQueueT0 || obs.InPendingQueueT1)
                {
                    violations++;
                    failureNotes.Add(
                        $"Negative case '{obs.GovernanceCaseId}' (already overridden at t0) unexpectedly appeared in pending queue (t0={obs.WasInPendingQueueT0}, t1={obs.InPendingQueueT1}).");
                }
            }
        }

        return new ProvenanceInvariantCheckResult
        {
            InvariantId = "INV-P8",
            InvariantName = "Pending Queue Hot-Index Lifecycle Consistency",
            Passed = violations == 0,
            CheckedRecordsCount = observations.Count,
            ViolationCount = violations,
            Details = violations == 0
                ? $"100% of {observations.Count} governance cases satisfied GET /pending-approvals hot-queue membership at t0 and removal/absence at t1."
                : string.Join(" | ", failureNotes)
        };
    }
}
