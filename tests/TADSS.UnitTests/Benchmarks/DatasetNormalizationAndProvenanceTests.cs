using FluentAssertions;
using TADSS.BenchmarkRunner.Export;
using TADSS.BenchmarkRunner.Harness;
using TADSS.BenchmarkRunner.Normalization;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.BenchmarkRunner.Telemetry;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;
using Xunit;

namespace TADSS.UnitTests.Benchmarks;

public sealed class DatasetNormalizationAndProvenanceTests
{
    private const string ValidSha256HashA = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string ValidSha256HashB = "f0e1d2c3b4a5968778695a4b3c2d1e0ff0e1d2c3b4a5968778695a4b3c2d1e0f";

    #region 1. Schema Column Count & Partitioning Verification (STEP 1 Contract)

    [Fact]
    public void FrozenCsvHeaders_MatchExactSpecificationColumnCounts_23_19_31()
    {
        var resilienceCols = DatasetArtifactExporter.ResilienceCsvHeader.Split(',');
        var cachingCols = DatasetArtifactExporter.LatencyCachingCsvHeader.Split(',');
        var governanceCols = DatasetArtifactExporter.GovernanceAuditCsvHeader.Split(',');

        resilienceCols.Should().HaveCount(23);
        resilienceCols.Should().Contain("need_approval");
        resilienceCols.Should().Contain("cache_hit");

        cachingCols.Should().HaveCount(19);
        cachingCols.Should().Contain("client_server_latency_delta_ms");
        cachingCols.Should().NotContain("transport_and_persistence_overhead_ms");

        governanceCols.Should().HaveCount(31);
        governanceCols.Should().Contain("governance_case_type");
        governanceCols.Should().Contain("version_transition");
        governanceCols.Should().NotContain("version_t0_to_t1");
    }

    [Fact]
    public void ScenarioPartitioning_SplitsAll19ScenariosCleanlyInto11ResilienceAnd8Caching()
    {
        var allScenarios = Enum.GetValues<ExperimentScenarioId>().Select(s => s.ToString()).ToList();
        allScenarios.Should().HaveCount(19);

        var resilienceScenarios = allScenarios.Where(DatasetNormalizerEngine.IsResilienceScenario).ToList();
        var cachingScenarios = allScenarios.Where(DatasetNormalizerEngine.IsLatencyCachingScenario).ToList();

        resilienceScenarios.Should().HaveCount(11);
        cachingScenarios.Should().HaveCount(8);
        resilienceScenarios.Intersect(cachingScenarios).Should().BeEmpty();
    }

    #endregion

    #region 2. Harvester & Normalizer Engine (STEP 4, STEP 5, STEP 6)

    [Fact]
    public void PartitionByMeasurementDecisionIds_ExcludesWarmupAndEmptyGuidRecordsCleanly()
    {
        var meta = CreateMetadata("EXP-W8-R0-VU001", "R0", BenchmarkDefaults.Layers.LayerBResilience, concurrency: 1);
        var summary = CreateSummary(meta);

        Guid measId1 = Guid.NewGuid();
        Guid measId2 = Guid.NewGuid();
        Guid warmupId = Guid.NewGuid();

        var clientRecords = new List<RawMeasurementRecord>
        {
            CreateClientRecord(meta.RunId, "R0", 0, measId1, ValidSha256HashA, "AUTOMATED", fallback: false, cacheHit: false),
            CreateClientRecord(meta.RunId, "R0", 1, measId2, ValidSha256HashB, "AUTOMATED", fallback: false, cacheHit: false),
            CreateClientRecord(meta.RunId, "R0", 2, Guid.Empty, ValidSha256HashA, "FAILED", fallback: false, cacheHit: false)
        };

        var dbExecutions = new List<DecisionExecution>
        {
            CreateAuditExecution(warmupId, "WARMUP-R0-0", ValidSha256HashA, "AUTOMATED", fallback: false, cacheHit: false, needApproval: false),
            CreateAuditExecution(measId1, $"{meta.RunId}-REQ-000000", ValidSha256HashA, "AUTOMATED", fallback: false, cacheHit: false, needApproval: false),
            CreateAuditExecution(measId2, $"{meta.RunId}-REQ-000001", ValidSha256HashB, "AUTOMATED", fallback: false, cacheHit: false, needApproval: false)
        };

        var harvest = AuditLedgerHarvester.PartitionByMeasurementDecisionIds(meta, summary, clientRecords, dbExecutions);

        harvest.TotalDbExecutionsCount.Should().Be(3);
        harvest.SelectedAuditExecutions.Should().HaveCount(2);
        harvest.ExcludedNonMeasurementExecutions.Should().ContainSingle(e => e.DecisionId == warmupId);

        var joined = DatasetNormalizerEngine.JoinRunRecords(harvest);
        joined.Should().HaveCount(2);
        joined.Select(j => j.AuditExecution.DecisionId).Should().Equal(measId1, measId2);
    }

    [Fact]
    public void MapToLatencyCachingRow_ComputesObservedLatencyDeltaCorrectly()
    {
        var meta = CreateMetadata("EXP-W8-G2-VU010", "G2", BenchmarkDefaults.Layers.LayerACache, concurrency: 10);
        var summary = CreateSummary(meta, cacheHitRatio: 100.0);
        Guid decisionId = Guid.NewGuid();

        var clientRec = CreateClientRecord(meta.RunId, "G2", 0, decisionId, ValidSha256HashA, "CACHE_HIT", fallback: false, cacheHit: true, clientMs: 4.25, serverMs: 0);
        var auditExec = CreateAuditExecution(decisionId, $"{meta.RunId}-REQ-000000", ValidSha256HashA, "CACHE_HIT", fallback: false, cacheHit: true, needApproval: false, execTimeMs: 0);

        var joined = new JoinedExperimentRecord
        {
            Metadata = meta,
            Summary = summary,
            ClientRecord = clientRec,
            AuditExecution = auditExec
        };

        var row = DatasetNormalizerEngine.MapToLatencyCachingRow(joined);

        row.SchemaVersion.Should().Be("1.0.0");
        row.CacheHit.Should().BeTrue();
        row.ExecutionType.Should().Be("CACHE_HIT");
        row.ClientElapsedMs.Should().Be(4.25);
        row.ServerExecutionTimeMs.Should().Be(0);
        row.ClientServerLatencyDeltaMs.Should().Be(4.25);
        row.WithinSla150Ms.Should().BeTrue();
    }

    #endregion

    #region 3. Provenance Invariants Verifier (STEP 7: INV-P1 .. INV-P8)

    [Fact]
    public void VerifyInvP1_DetectsDuplicateDecisionIdsWithinSameRun()
    {
        var meta = CreateMetadata("EXP-W8-R0-VU001", "R0", BenchmarkDefaults.Layers.LayerBResilience, concurrency: 1);
        var summary = CreateSummary(meta);
        Guid duplicateId = Guid.NewGuid();

        var harvest = new AuditHarvestResult
        {
            Metadata = meta,
            Summary = summary,
            MeasurementRecords =
            [
                CreateClientRecord(meta.RunId, "R0", 0, duplicateId, ValidSha256HashA, "AUTOMATED", false, false),
                CreateClientRecord(meta.RunId, "R0", 1, duplicateId, ValidSha256HashA, "AUTOMATED", false, false)
            ],
            SelectedAuditExecutions =
            [
                CreateAuditExecution(duplicateId, $"{meta.RunId}-REQ-000000", ValidSha256HashA, "AUTOMATED", false, false, false)
            ],
            ExcludedNonMeasurementExecutions = [],
            TotalDbExecutionsCount = 1
        };

        var result = DatasetProvenanceVerifier.VerifyInvP1BijectiveEligibleJoin([harvest]);
        result.Passed.Should().BeFalse();
        result.ViolationCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void VerifyInvP2_DetectsWarmupContaminationInSelectedExecutions()
    {
        var meta = CreateMetadata("EXP-W8-R0-VU001", "R0", BenchmarkDefaults.Layers.LayerBResilience, concurrency: 1);
        var summary = CreateSummary(meta);
        Guid contaminatedId = Guid.NewGuid();

        var harvest = new AuditHarvestResult
        {
            Metadata = meta,
            Summary = summary,
            MeasurementRecords =
            [
                CreateClientRecord(meta.RunId, "R0", 0, contaminatedId, ValidSha256HashA, "AUTOMATED", false, false)
            ],
            SelectedAuditExecutions =
            [
                CreateAuditExecution(contaminatedId, "WARMUP-R0-CONTAMINATED", ValidSha256HashA, "AUTOMATED", false, false, false)
            ],
            ExcludedNonMeasurementExecutions = [],
            TotalDbExecutionsCount = 1
        };

        var result = DatasetProvenanceVerifier.VerifyInvP2ZeroWarmupContamination([harvest]);
        result.Passed.Should().BeFalse();
        result.ViolationCount.Should().Be(1);
    }

    [Fact]
    public void VerifyInvP3aAndP3b_DetectsInvalidHashFormatAndCanonicalHashMismatch()
    {
        var meta = CreateMetadata("EXP-W8-G2-VU001", "G2", BenchmarkDefaults.Layers.LayerACache, concurrency: 1);
        var summary = CreateSummary(meta);
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();

        var rec1 = CreateClientRecord(meta.RunId, "G2", 0, id1, ValidSha256HashA, "CACHE_HIT", false, true);
        var exec1 = CreateAuditExecution(id1, $"{meta.RunId}-REQ-000000", "INVALID_NON_HEX_HASH", "CACHE_HIT", false, true, false);

        var rec2 = CreateClientRecord(meta.RunId, "G2", 1, id2, ValidSha256HashA, "CACHE_HIT", false, true);
        var exec2 = CreateAuditExecution(id2, $"{meta.RunId}-REQ-000001", ValidSha256HashB, "CACHE_HIT", false, true, false);

        var joined = new List<JoinedExperimentRecord>
        {
            new() { Metadata = meta, Summary = summary, ClientRecord = rec1, AuditExecution = exec1 },
            new() { Metadata = meta, Summary = summary, ClientRecord = rec2, AuditExecution = exec2 }
        };

        var harvest = new AuditHarvestResult
        {
            Metadata = meta,
            Summary = summary,
            MeasurementRecords = [rec1, rec2],
            SelectedAuditExecutions = [exec1, exec2],
            ExcludedNonMeasurementExecutions = [],
            TotalDbExecutionsCount = 2
        };

        DatasetProvenanceVerifier.VerifyInvP3aSha256FormatIntegrity(joined).Passed.Should().BeFalse();
        DatasetProvenanceVerifier.VerifyInvP3bRfc8785CanonicalDeterminism([harvest], joined).Passed.Should().BeFalse();
    }

    [Fact]
    public void VerifyInvP4AndP5_DetectsTelemetryDiscrepancyAndFallbackSafetyViolation()
    {
        var meta = CreateMetadata("EXP-W8-R3-VU001", "R3", BenchmarkDefaults.Layers.LayerBResilience, concurrency: 1);
        var summary = CreateSummary(meta);
        Guid id = Guid.NewGuid();

        var clientRec = CreateClientRecord(meta.RunId, "R3", 0, id, ValidSha256HashA, "FALLBACK", fallback: true, cacheHit: false);
        // Violates INV-P4 (ExecutionType mismatch) and INV-P5 (FallbackUsed=true but NeedApproval=false)
        var badExec = CreateAuditExecution(id, $"{meta.RunId}-REQ-000000", ValidSha256HashA, "AUTOMATED", fallback: true, cacheHit: false, needApproval: false);

        var joined = new List<JoinedExperimentRecord>
        {
            new() { Metadata = meta, Summary = summary, ClientRecord = clientRec, AuditExecution = badExec }
        };

        DatasetProvenanceVerifier.VerifyInvP4ClientToLedgerConsistency(joined).Passed.Should().BeFalse();
        DatasetProvenanceVerifier.VerifyInvP5FallbackSafetyPolicy(joined).Passed.Should().BeFalse();
    }

    [Fact]
    public void VerifyInvP6_P7_P8_ValidatesPositiveAndNegativeGovernanceControlsCorrectly()
    {
        Guid posId = Guid.NewGuid();
        var t0Pos = CreateAuditExecution(posId, "GOV-GA-001", ValidSha256HashA, "FALLBACK", fallback: true, cacheHit: false, needApproval: true);
        var t1Pos = AuditLedgerHarvester.CloneExecution(t0Pos);
        t1Pos.IsOverridden = true;
        t1Pos.OverriddenBy = "manager@smashcourt.vn";
        t1Pos.OverrideReason = "Approved after manual verification.";
        t1Pos.Version = 2;
        t1Pos.UpdatedAt = t0Pos.UpdatedAt.AddSeconds(5);

        Guid negId = Guid.NewGuid();
        var t0Neg = CreateAuditExecution(negId, "GOV-GC2-001", ValidSha256HashB, "AUTOMATED", fallback: false, cacheHit: false, needApproval: false);
        var t1Neg = AuditLedgerHarvester.CloneExecution(t0Neg);

        var observations = new List<GovernanceLifecycleObservation>
        {
            new()
            {
                GovernanceCaseId = "G-A-001",
                GovernanceScenarioGroup = "G-A_FALLBACK_OVERRIDE",
                GovernanceCaseType = "POSITIVE_CONTROL",
                SnapshotT0 = t0Pos,
                SnapshotT1 = t1Pos,
                WasInPendingQueueT0 = true,
                InPendingQueueT1 = false,
                OverrideHttpStatus = 200
            },
            new()
            {
                GovernanceCaseId = "G-C2-001",
                GovernanceScenarioGroup = "G-C2_NON_APPROVABLE_GUARD",
                GovernanceCaseType = "NEGATIVE_CONTROL",
                SnapshotT0 = t0Neg,
                SnapshotT1 = t1Neg,
                WasInPendingQueueT0 = false,
                InPendingQueueT1 = false,
                OverrideHttpStatus = 400
            }
        };

        DatasetProvenanceVerifier.VerifyInvP6MachineSnapshotImmutability(observations).Passed.Should().BeTrue();
        DatasetProvenanceVerifier.VerifyInvP7GovernanceTransitionAndGuardPreservation(observations).Passed.Should().BeTrue();
        DatasetProvenanceVerifier.VerifyInvP8PendingQueueHotIndexLifecycle(observations).Passed.Should().BeTrue();

        // Now corrupt ResponsePayload at t1 -> INV-P6 must fail
        var corruptedT1 = AuditLedgerHarvester.CloneExecution(t1Pos);
        corruptedT1.ResponsePayload = "{\"tampered\":true}";
        var corruptedObs = new List<GovernanceLifecycleObservation>
        {
            new()
            {
                GovernanceCaseId = "G-A-CORRUPT",
                GovernanceScenarioGroup = "G-A_FALLBACK_OVERRIDE",
                GovernanceCaseType = "POSITIVE_CONTROL",
                SnapshotT0 = t0Pos,
                SnapshotT1 = corruptedT1,
                WasInPendingQueueT0 = true,
                InPendingQueueT1 = false,
                OverrideHttpStatus = 200
            }
        };

        DatasetProvenanceVerifier.VerifyInvP6MachineSnapshotImmutability(corruptedObs).Passed.Should().BeFalse();
    }

    #endregion

    #region 4. End-to-End Live Harvest, Governance Harness & Artifact Export Integration Test

    [Fact]
    public async Task EndToEndNormalizationPipeline_HarvestsJoinsVerifiesAndExportsAllArtifacts()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"tadss_w9_test_{Guid.NewGuid():N}");
        try
        {
            var config = new BenchmarkSuiteConfig
            {
                Scenarios = [ExperimentScenarioId.G2, ExperimentScenarioId.R2],
                ConcurrencyLevels = [1],
                FastScenarioRequests = 5,
                FaultTimedScenarioRequests = 5,
                WarmupRequests = 2,
                DatasetsOutputDirectory = tempDir
            };

            var harvestResults = new List<AuditHarvestResult>();

            foreach (var scenario in config.Scenarios)
            {
                await using var hostController = new ScenarioHostController(scenario, concurrency: 1, config);
                using var client = hostController.CreateBenchmarkClient(out var metadata, totalRequests: 5, warmupRequests: 2);

                await hostController.ExecuteWarmupAndLockStateAsync(client, warmupRequests: 2);

                var worker = new MeasurementWorker(hostController, client, metadata, totalRequests: 5, concurrency: 1);
                var (records, wallClockMs) = await worker.RunAllAsync();

                var summary = ResultAggregator.Aggregate(
                    metadata,
                    records,
                    wallClockMs,
                    hostController.StrategyInvocationCount,
                    hostController.ObservedRetryCount,
                    hostController.ObservedInnerTimeoutCount);

                var harvest = await AuditLedgerHarvester.HarvestFromLiveHostAsync(hostController, metadata, summary, records);
                harvestResults.Add(harvest);
            }

            var govObservations = await GovernanceLifecycleHarness.ExecuteSuiteAsync(
                config,
                positiveFallbackCases: 2,
                positiveAutomatedReviewCases: 2,
                negativeDuplicateCases: 1,
                negativeNonApprovableCases: 1);

            govObservations.Should().HaveCount(6);

            var joined = DatasetNormalizerEngine.JoinAllRuns(harvestResults);
            joined.Should().HaveCount(10);

            var (resilienceRows, cachingRows) = DatasetNormalizerEngine.ProjectResilienceAndCachingDatasets(joined);
            var govRows = DatasetNormalizerEngine.ProjectGovernanceAuditDataset(govObservations);

            resilienceRows.Should().HaveCount(5);
            cachingRows.Should().HaveCount(5);
            govRows.Should().HaveCount(6);

            var invariants = DatasetProvenanceVerifier.VerifyAll(harvestResults, joined, govObservations);
            invariants.Should().AllSatisfy(i => i.Passed.Should().BeTrue(because: $"{i.InvariantId} failed: {i.Details}"));

            var manifest = await DatasetArtifactExporter.ExportAllAsync(
                tempDir,
                harvestResults,
                resilienceRows,
                cachingRows,
                govRows,
                invariants);

            manifest.AllProvenanceInvariantsPassed.Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "resilience_evaluation_dataset.csv")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "resilience_evaluation_dataset.jsonl")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "latency_caching_dataset.csv")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "latency_caching_dataset.jsonl")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "governance_audit_dataset.csv")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "governance_audit_dataset.jsonl")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "dataset_provenance_manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "WEEK9_DATASET_NORMALIZATION_REPORT.md")).Should().BeTrue();

            foreach (var datasetDesc in manifest.Datasets)
            {
                byte[] csvBytes = await File.ReadAllBytesAsync(Path.Combine(tempDir, datasetDesc.CsvFileName));
                string actualCsvSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(csvBytes)).ToLowerInvariant();
                actualCsvSha256.Should().Be(datasetDesc.CsvSha256);

                byte[] jsonlBytes = await File.ReadAllBytesAsync(Path.Combine(tempDir, datasetDesc.JsonlFileName));
                string actualJsonlSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jsonlBytes)).ToLowerInvariant();
                actualJsonlSha256.Should().Be(datasetDesc.JsonlSha256);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    #endregion

    #region Helpers

    private static RunMetadata CreateMetadata(string runId, string scenario, string layer, int concurrency) =>
        new()
        {
            RunId = runId,
            TimestampUtc = "2026-09-29T15:00:00Z",
            Scenario = scenario,
            ExperimentLayer = layer,
            Concurrency = concurrency,
            TotalRequests = 10,
            WarmupRequests = 2,
            Strategy = "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            CacheState = "warm_hit_path",
            RedisState = "in_process_hermetic",
            DatabaseState = "isolated_in_memory",
            EnvironmentProfile = "ControlledSelfHosted",
            MachineName = "TEST-HOST",
            OsDescription = "Windows",
            CpuCores = 8,
            DotNetVersion = ".NET 8.0",
            OuterTimeoutMs = 500,
            InnerTimeoutMs = 120,
            RetryCount = 2,
            RetryDelayMs = 30,
            ConfiguredResilienceBudgetMs = 420,
            CircuitStateSetup = "CLOSED"
        };

    private static ScenarioSummaryMetrics CreateSummary(RunMetadata metadata, double cacheHitRatio = 0.0) =>
        new()
        {
            Metadata = metadata,
            TotalWallClockMs = 10.0,
            ThroughputRps = 100.0,
            ClientP50Ms = 1.0,
            ClientP90Ms = 2.0,
            ClientP95Ms = 2.5,
            ClientP99Ms = 3.0,
            ClientMaxMs = 4.0,
            ClientMeanMs = 1.5,
            ClientStdDevMs = 0.5,
            ServerP50Ms = 0.0,
            ServerP90Ms = 1.0,
            ServerP95Ms = 1.0,
            ServerP99Ms = 1.0,
            ServerMaxMs = 1.0,
            ServerMeanMs = 0.5,
            EligibleRequests = 10,
            CompletedSafeDecisions = 10,
            DcrPercent = 100.0,
            CacheHitRatioPercent = cacheHitRatio,
            FallbackRatePercent = 0.0,
            HttpErrorOrTimeoutCount = 0,
            HttpErrorRatePercent = 0.0,
            StrategyInvocations = 10,
            ObservedRetryCount = 0,
            ObservedInnerTimeoutCount = 0,
            CircuitStateObserved = "CLOSED"
        };

    private static RawMeasurementRecord CreateClientRecord(
        string runId,
        string scenario,
        int index,
        Guid decisionId,
        string expectedHash,
        string executionType,
        bool fallback,
        bool cacheHit,
        double clientMs = 2.5,
        int serverMs = 1) =>
        new()
        {
            RunId = runId,
            Scenario = scenario,
            Concurrency = 1,
            RequestIndex = index,
            DecisionId = decisionId,
            ReservationId = Guid.NewGuid(),
            ExpectedRequestHash = expectedHash,
            ClientElapsedMs = clientMs,
            ServerExecutionTimeMs = serverMs,
            HttpStatusCode = 200,
            DecisionStatus = fallback ? "NEED_APPROVAL" : "APPROVED",
            ExecutionType = executionType,
            FallbackUsed = fallback,
            CacheHit = cacheHit,
            IsEligibleForDcr = true,
            IsSafeCompletedDecision = decisionId != Guid.Empty
        };

    private static DecisionExecution CreateAuditExecution(
        Guid decisionId,
        string idempotencyKey,
        string requestHash,
        string executionType,
        bool fallback,
        bool cacheHit,
        bool needApproval,
        int execTimeMs = 1)
    {
        var now = new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
        return new DecisionExecution
        {
            Id = Guid.NewGuid(),
            DecisionId = decisionId,
            ReservationId = Guid.NewGuid(),
            StrategyName = fallback ? "FallbackDecisionStrategy" : "DefaultDecisionStrategy",
            StrategyVersion = "1.0.0",
            ExecutionType = executionType,
            ExecutionTimeMs = execTimeMs,
            FallbackUsed = fallback,
            FailureReason = fallback ? "CIRCUIT_OPEN" : null,
            CacheHit = cacheHit,
            NeedApproval = needApproval,
            RequestHash = requestHash,
            ResponsePayload = "{\"status\":\"APPROVED\"}",
            IdempotencyKey = idempotencyKey,
            IsOverridden = false,
            OverriddenBy = null,
            OverrideReason = null,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    #endregion
}
