using System.Text.Json.Serialization;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Data.Entities;

namespace TADSS.BenchmarkRunner.Normalization;

/// <summary>
/// Kết quả thu hoạch sổ cái kiểm toán (In-Vivo Audit Ledger Harvest) của một lượt chạy (Scenario x Concurrency)
/// ngay trước khi ScenarioHostController.DisposeAsync() giải phóng In-Memory Database.
/// </summary>
public sealed class AuditHarvestResult
{
    public required RunMetadata Metadata { get; init; }
    public required ScenarioSummaryMetrics Summary { get; init; }
    public required IReadOnlyList<RawMeasurementRecord> MeasurementRecords { get; init; }
    public required IReadOnlyList<DecisionExecution> SelectedAuditExecutions { get; init; }
    public required IReadOnlyList<DecisionExecution> ExcludedNonMeasurementExecutions { get; init; }
    public required int TotalDbExecutionsCount { get; init; }
}

/// <summary>
/// Bản ghi trung gian kết nối 1:1 (Bijective Inner Join) giữa RawMeasurementRecord ở Client
/// và DecisionExecution trong sổ cái kiểm toán Server thông qua khóa logic DecisionId.
/// </summary>
public sealed class JoinedExperimentRecord
{
    public required RunMetadata Metadata { get; init; }
    public required ScenarioSummaryMetrics Summary { get; init; }
    public required RawMeasurementRecord ClientRecord { get; init; }
    public required DecisionExecution AuditExecution { get; init; }
}

/// <summary>
/// Schema 2.1 — Bộ dữ liệu Đánh giá Khả năng Chịu lỗi (resilience_evaluation_dataset — 23 cột đóng băng).
/// Phục vụ Câu hỏi Nghiên cứu Kỹ thuật RQ1.
/// </summary>
public sealed class ResilienceEvaluationDatasetRow
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = BenchmarkDefaults.Normalization.SchemaVersion;

    [JsonPropertyOrder(2)]
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("experiment_layer")]
    public required string ExperimentLayer { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName("scenario_id")]
    public required string ScenarioId { get; init; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName("concurrency_level")]
    public required int ConcurrencyLevel { get; init; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName("sample_index")]
    public required int SampleIndex { get; init; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("decision_id")]
    public required Guid DecisionId { get; init; }

    [JsonPropertyOrder(8)]
    [JsonPropertyName("reservation_id")]
    public required Guid ReservationId { get; init; }

    [JsonPropertyOrder(9)]
    [JsonPropertyName("strategy_name")]
    public required string StrategyName { get; init; }

    [JsonPropertyOrder(10)]
    [JsonPropertyName("strategy_version")]
    public required string StrategyVersion { get; init; }

    [JsonPropertyOrder(11)]
    [JsonPropertyName("execution_type")]
    public required string ExecutionType { get; init; }

    [JsonPropertyOrder(12)]
    [JsonPropertyName("decision_status")]
    public required string DecisionStatus { get; init; }

    [JsonPropertyOrder(13)]
    [JsonPropertyName("fallback_used")]
    public required bool FallbackUsed { get; init; }

    [JsonPropertyOrder(14)]
    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    [JsonPropertyOrder(15)]
    [JsonPropertyName("need_approval")]
    public required bool NeedApproval { get; init; }

    [JsonPropertyOrder(16)]
    [JsonPropertyName("cache_hit")]
    public required bool CacheHit { get; init; }

    [JsonPropertyOrder(17)]
    [JsonPropertyName("http_status_code")]
    public required int HttpStatusCode { get; init; }

    [JsonPropertyOrder(18)]
    [JsonPropertyName("client_elapsed_ms")]
    public required double ClientElapsedMs { get; init; }

    [JsonPropertyOrder(19)]
    [JsonPropertyName("server_execution_time_ms")]
    public required int ServerExecutionTimeMs { get; init; }

    [JsonPropertyOrder(20)]
    [JsonPropertyName("within_configured_outer_sla")]
    public required bool WithinConfiguredOuterSla { get; init; }

    [JsonPropertyOrder(21)]
    [JsonPropertyName("circuit_setup_mode")]
    public required string CircuitSetupMode { get; init; }

    [JsonPropertyOrder(22)]
    [JsonPropertyName("is_eligible_for_dcr")]
    public required bool IsEligibleForDcr { get; init; }

    [JsonPropertyOrder(23)]
    [JsonPropertyName("created_at_utc")]
    public required string CreatedAtUtc { get; init; }
}

/// <summary>
/// Schema 2.2 — Bộ dữ liệu Độ trễ và Bộ nhớ đệm (latency_caching_dataset — 19 cột đóng băng).
/// Phục vụ Câu hỏi Nghiên cứu Kỹ thuật RQ2.
/// </summary>
public sealed class LatencyCachingDatasetRow
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = BenchmarkDefaults.Normalization.SchemaVersion;

    [JsonPropertyOrder(2)]
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("experiment_layer")]
    public required string ExperimentLayer { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName("scenario_id")]
    public required string ScenarioId { get; init; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName("concurrency_level")]
    public required int ConcurrencyLevel { get; init; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName("sample_index")]
    public required int SampleIndex { get; init; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("decision_id")]
    public required Guid DecisionId { get; init; }

    [JsonPropertyOrder(8)]
    [JsonPropertyName("reservation_id")]
    public required Guid ReservationId { get; init; }

    [JsonPropertyOrder(9)]
    [JsonPropertyName("cache_state_mode")]
    public required string CacheStateMode { get; init; }

    [JsonPropertyOrder(10)]
    [JsonPropertyName("cache_hit")]
    public required bool CacheHit { get; init; }

    [JsonPropertyOrder(11)]
    [JsonPropertyName("execution_type")]
    public required string ExecutionType { get; init; }

    [JsonPropertyOrder(12)]
    [JsonPropertyName("request_hash")]
    public required string RequestHash { get; init; }

    [JsonPropertyOrder(13)]
    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyOrder(14)]
    [JsonPropertyName("client_elapsed_ms")]
    public required double ClientElapsedMs { get; init; }

    [JsonPropertyOrder(15)]
    [JsonPropertyName("server_execution_time_ms")]
    public required int ServerExecutionTimeMs { get; init; }

    [JsonPropertyOrder(16)]
    [JsonPropertyName("client_server_latency_delta_ms")]
    public required double ClientServerLatencyDeltaMs { get; init; }

    [JsonPropertyOrder(17)]
    [JsonPropertyName("run_cache_hit_ratio_percent")]
    public required double RunCacheHitRatioPercent { get; init; }

    [JsonPropertyOrder(18)]
    [JsonPropertyName("within_sla_150ms")]
    public required bool WithinSla150Ms { get; init; }

    [JsonPropertyOrder(19)]
    [JsonPropertyName("created_at_utc")]
    public required string CreatedAtUtc { get; init; }
}

/// <summary>
/// Bản ghi quan sát vòng đời quản trị kết quả quyết định trong bộ nhớ (bao gồm bản chụp thực thể đầy đủ tại t0 và t1)
/// để DatasetProvenanceVerifier kiểm định trực tiếp cả 9 trường Machine Snapshot + NeedApproval (INV-P6)
/// và 5 trường Mutable Governance Metadata (INV-P7) trước khi chiếu sang 31 cột của GovernanceAuditDatasetRow.
/// </summary>
public sealed class GovernanceLifecycleObservation
{
    public required string GovernanceCaseId { get; init; }
    public required string GovernanceScenarioGroup { get; init; }
    public required string GovernanceCaseType { get; init; } // "POSITIVE_CONTROL" | "NEGATIVE_CONTROL"
    public required DecisionExecution SnapshotT0 { get; init; }
    public required DecisionExecution SnapshotT1 { get; init; }
    public required bool WasInPendingQueueT0 { get; init; }
    public required bool InPendingQueueT1 { get; init; }
    public required int OverrideHttpStatus { get; init; }
}

/// <summary>
/// Schema 2.3 — Bộ dữ liệu Bằng chứng Chuyển trạng thái Quản trị & Kiểm toán Hai tầng
/// (governance_audit_dataset — 31 cột đóng băng, Transition-Evidence Dataset).
/// Phục vụ Câu hỏi Nghiên cứu Kỹ thuật RQ3.
/// </summary>
public sealed class GovernanceAuditDatasetRow
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = BenchmarkDefaults.Normalization.SchemaVersion;

    [JsonPropertyOrder(2)]
    [JsonPropertyName("governance_case_id")]
    public required string GovernanceCaseId { get; init; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName("governance_scenario_group")]
    public required string GovernanceScenarioGroup { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName("governance_case_type")]
    public required string GovernanceCaseType { get; init; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName("decision_id")]
    public required Guid DecisionId { get; init; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName("reservation_id")]
    public required Guid ReservationId { get; init; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName("strategy_name_t0")]
    public required string StrategyNameT0 { get; init; }

    [JsonPropertyOrder(8)]
    [JsonPropertyName("strategy_name_t1")]
    public required string StrategyNameT1 { get; init; }

    [JsonPropertyOrder(9)]
    [JsonPropertyName("strategy_version_t0")]
    public required string StrategyVersionT0 { get; init; }

    [JsonPropertyOrder(10)]
    [JsonPropertyName("strategy_version_t1")]
    public required string StrategyVersionT1 { get; init; }

    [JsonPropertyOrder(11)]
    [JsonPropertyName("execution_type_t0")]
    public required string ExecutionTypeT0 { get; init; }

    [JsonPropertyOrder(12)]
    [JsonPropertyName("execution_type_t1")]
    public required string ExecutionTypeT1 { get; init; }

    [JsonPropertyOrder(13)]
    [JsonPropertyName("execution_time_ms_t0")]
    public required int ExecutionTimeMsT0 { get; init; }

    [JsonPropertyOrder(14)]
    [JsonPropertyName("execution_time_ms_t1")]
    public required int ExecutionTimeMsT1 { get; init; }

    [JsonPropertyOrder(15)]
    [JsonPropertyName("request_hash_t0")]
    public required string RequestHashT0 { get; init; }

    [JsonPropertyOrder(16)]
    [JsonPropertyName("request_hash_t1")]
    public required string RequestHashT1 { get; init; }

    [JsonPropertyOrder(17)]
    [JsonPropertyName("response_payload_sha256_t0")]
    public required string ResponsePayloadSha256T0 { get; init; }

    [JsonPropertyOrder(18)]
    [JsonPropertyName("response_payload_sha256_t1")]
    public required string ResponsePayloadSha256T1 { get; init; }

    [JsonPropertyOrder(19)]
    [JsonPropertyName("created_at_utc_t0")]
    public required string CreatedAtUtcT0 { get; init; }

    [JsonPropertyOrder(20)]
    [JsonPropertyName("created_at_utc_t1")]
    public required string CreatedAtUtcT1 { get; init; }

    [JsonPropertyOrder(21)]
    [JsonPropertyName("need_approval_t0")]
    public required bool NeedApprovalT0 { get; init; }

    [JsonPropertyOrder(22)]
    [JsonPropertyName("need_approval_t1")]
    public required bool NeedApprovalT1 { get; init; }

    [JsonPropertyOrder(23)]
    [JsonPropertyName("was_in_pending_queue_t0")]
    public required bool WasInPendingQueueT0 { get; init; }

    [JsonPropertyOrder(24)]
    [JsonPropertyName("in_pending_queue_t1")]
    public required bool InPendingQueueT1 { get; init; }

    [JsonPropertyOrder(25)]
    [JsonPropertyName("override_http_status")]
    public required int OverrideHttpStatus { get; init; }

    [JsonPropertyOrder(26)]
    [JsonPropertyName("is_overridden_t0")]
    public required bool IsOverriddenT0 { get; init; }

    [JsonPropertyOrder(27)]
    [JsonPropertyName("is_overridden_t1")]
    public required bool IsOverriddenT1 { get; init; }

    [JsonPropertyOrder(28)]
    [JsonPropertyName("overridden_by_t1")]
    public string? OverriddenByT1 { get; init; }

    [JsonPropertyOrder(29)]
    [JsonPropertyName("override_reason_t1")]
    public string? OverrideReasonT1 { get; init; }

    [JsonPropertyOrder(30)]
    [JsonPropertyName("version_transition")]
    public required string VersionTransition { get; init; }

    [JsonPropertyOrder(31)]
    [JsonPropertyName("machine_snapshot_immutable_verified")]
    public required bool MachineSnapshotImmutableVerified { get; init; }
}

/// <summary>
/// Kết quả kiểm định một bất biến truy xuất nguồn gốc (INV-P1 .. INV-P8).
/// </summary>
public sealed class ProvenanceInvariantCheckResult
{
    [JsonPropertyName("invariant_id")]
    public required string InvariantId { get; init; }

    [JsonPropertyName("invariant_name")]
    public required string InvariantName { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("checked_records_count")]
    public required int CheckedRecordsCount { get; init; }

    [JsonPropertyName("violation_count")]
    public required int ViolationCount { get; init; }

    [JsonPropertyName("details")]
    public required string Details { get; init; }
}

/// <summary>
/// Thông tin kiểm kê tệp dữ liệu đã xuất trong biên bản dataset_provenance_manifest.json.
/// </summary>
public sealed class ExportedDatasetFileDescriptor
{
    [JsonPropertyName("dataset_name")]
    public required string DatasetName { get; init; }

    [JsonPropertyName("research_question")]
    public required string ResearchQuestion { get; init; }

    [JsonPropertyName("column_count")]
    public required int ColumnCount { get; init; }

    [JsonPropertyName("row_count")]
    public required int RowCount { get; init; }

    [JsonPropertyName("csv_file_name")]
    public required string CsvFileName { get; init; }

    [JsonPropertyName("csv_sha256")]
    public required string CsvSha256 { get; init; }

    [JsonPropertyName("jsonl_file_name")]
    public required string JsonlFileName { get; init; }

    [JsonPropertyName("jsonl_sha256")]
    public required string JsonlSha256 { get; init; }
}

/// <summary>
/// Biên bản tổng thể kiểm định nguồn gốc dữ liệu nghiên cứu (dataset_provenance_manifest.json).
/// </summary>
public sealed class DatasetProvenanceManifest
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = BenchmarkDefaults.Normalization.SchemaVersion;

    [JsonPropertyName("generated_at_utc")]
    public required string GeneratedAtUtc { get; init; }

    [JsonPropertyName("total_benchmark_runs_harvested")]
    public required int TotalBenchmarkRunsHarvested { get; init; }

    [JsonPropertyName("total_db_executions_observed")]
    public required int TotalDbExecutionsObserved { get; init; }

    [JsonPropertyName("excluded_warmup_and_trip_executions")]
    public required int ExcludedWarmupAndTripExecutions { get; init; }

    [JsonPropertyName("selected_measurement_executions")]
    public required int SelectedMeasurementExecutions { get; init; }

    [JsonPropertyName("governance_lifecycle_cases_executed")]
    public required int GovernanceLifecycleCasesExecuted { get; init; }

    [JsonPropertyName("all_provenance_invariants_passed")]
    public required bool AllProvenanceInvariantsPassed { get; init; }

    [JsonPropertyName("invariant_results")]
    public required IReadOnlyList<ProvenanceInvariantCheckResult> InvariantResults { get; init; }

    [JsonPropertyName("datasets")]
    public required IReadOnlyList<ExportedDatasetFileDescriptor> Datasets { get; init; }
}
