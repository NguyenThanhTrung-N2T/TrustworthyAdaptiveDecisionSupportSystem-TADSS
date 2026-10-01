using System.Text.Json.Serialization;

namespace TADSS.BenchmarkRunner.Protocol;

public enum ExperimentScenarioId
{
    // Layer A: Cache Baseline
    G0,
    G1,
    G2,

    // Layer B: Resilience & Fault Isolation
    R0,
    R1,
    R2,
    R3,

    // Layer C: Real-World Cache Pathologies
    C0,
    C1,
    C2,
    C3,

    // Layer D: Strategy & Persistence Performance
    D0,
    D1,
    D2,

    // Layer E: Combined Stress & Self-Healing
    E0,
    E1,
    E2,
    E3,
    E4
}

public readonly record struct LatencyRangeMs(int MinMs, int MaxMs)
{
    public static readonly LatencyRangeMs Zero = new(0, 0);
    public bool IsEnabled => MaxMs > 0;

    public int ComputeDeterministicDelayMs(int seedIndex)
    {
        if (MaxMs <= MinMs) return Math.Max(0, MinMs);
        int span = MaxMs - MinMs + 1;
        return MinMs + (Math.Abs(seedIndex * 1103515245 + 12345) % span);
    }
}

/// <summary>
/// Cấu hình thông số đường ống chịu lỗi Polly v8 cho thực nghiệm (không hardcode trong code điều phối).
/// </summary>
public sealed class BenchmarkResilienceSettings
{
    public int OuterTimeoutMs { get; init; } = BenchmarkDefaults.Resilience.OuterTimeoutMs;
    public int InnerTimeoutMs { get; init; } = BenchmarkDefaults.Resilience.InnerTimeoutMs;
    public int RetryCount { get; init; } = BenchmarkDefaults.Resilience.RetryCount;
    public int RetryDelayMs { get; init; } = BenchmarkDefaults.Resilience.RetryDelayMs;
    public int SamplingDurationSeconds { get; init; } = BenchmarkDefaults.Resilience.SamplingDurationSeconds;
    public int StandardMinimumThroughput { get; init; } = BenchmarkDefaults.Resilience.StandardMinimumThroughput;
    public double StandardFailureRatio { get; init; } = BenchmarkDefaults.Resilience.StandardFailureRatio;
    public int StandardBreakDurationSeconds { get; init; } = BenchmarkDefaults.Resilience.StandardBreakDurationSeconds;
    public int PreOpenedMinimumThroughput { get; init; } = BenchmarkDefaults.Resilience.PreOpenedMinimumThroughput;
    public int PreOpenedBreakDurationSeconds { get; init; } = BenchmarkDefaults.Resilience.PreOpenedBreakDurationSeconds;
    public int PreOpenTripRequestCount { get; init; } = BenchmarkDefaults.Resilience.PreOpenTripRequestCount;
    public int HalfOpenRecoveryBreakDurationSeconds { get; init; } = BenchmarkDefaults.Resilience.HalfOpenRecoveryBreakDurationSeconds;
    public int HalfOpenRecoveryWaitBufferMs { get; init; } = BenchmarkDefaults.Resilience.HalfOpenRecoveryWaitBufferMs;

    public int ComputedAttemptAndDelayBudgetMs =>
        (RetryCount + 1) * InnerTimeoutMs + RetryCount * RetryDelayMs;
}

/// <summary>
/// Cấu hình độ trễ tiêm lỗi (Fault Injection Timing) cho các kịch bản thực nghiệm có kiểm soát (Layers B, C, D, E).
/// </summary>
public sealed class BenchmarkFaultTimingSettings
{
    public int RecoverableFirstAttemptDelayMs { get; init; } = BenchmarkDefaults.FaultTiming.RecoverableFirstAttemptDelayMs;
    public int ExhaustiveAttemptDelayMs { get; init; } = BenchmarkDefaults.FaultTiming.ExhaustiveAttemptDelayMs;
    public int TimedScenarioMaxWarmupRequests { get; init; } = BenchmarkDefaults.FaultTiming.TimedScenarioMaxWarmupRequests;

    // Layer C
    public LatencyRangeMs WanCacheLatency { get; init; } = new(
        BenchmarkDefaults.FaultTiming.WanCacheMinLatencyMs,
        BenchmarkDefaults.FaultTiming.WanCacheMaxLatencyMs);
    public LatencyRangeMs ThunderingHerdColdComputeDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.ThunderingHerdColdComputeMinMs,
        BenchmarkDefaults.FaultTiming.ThunderingHerdColdComputeMaxMs);
    public int BoundedCacheCapacity { get; init; } = BenchmarkDefaults.FaultTiming.BoundedCacheCapacity;
    public int BoundedCacheWorkingSetModulo { get; init; } = BenchmarkDefaults.FaultTiming.BoundedCacheWorkingSetModulo;

    // Layer D
    public LatencyRangeMs HeavyStrategyDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.HeavyStrategyMinDelayMs,
        BenchmarkDefaults.FaultTiming.HeavyStrategyMaxDelayMs);
    public LatencyRangeMs DbContentionDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.DbContentionMinDelayMs,
        BenchmarkDefaults.FaultTiming.DbContentionMaxDelayMs);
    public int JitterFastDelayMs { get; init; } = BenchmarkDefaults.FaultTiming.JitterFastDelayMs;
    public int JitterSlowDelayMs { get; init; } = BenchmarkDefaults.FaultTiming.JitterSlowDelayMs;
    public int JitterSlowRequestModulo { get; init; } = BenchmarkDefaults.FaultTiming.JitterSlowRequestModulo;

    // Layer E
    public int CompositeHitModulo { get; init; } = BenchmarkDefaults.FaultTiming.CompositeHitModulo;
    public LatencyRangeMs CompositeFastStrategyDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.CompositeFastStrategyMinMs,
        BenchmarkDefaults.FaultTiming.CompositeFastStrategyMaxMs);
    public LatencyRangeMs CompositeFastDbDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.CompositeFastDbMinMs,
        BenchmarkDefaults.FaultTiming.CompositeFastDbMaxMs);
    public LatencyRangeMs CompoundWanCacheDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.CompoundWanCacheMinMs,
        BenchmarkDefaults.FaultTiming.CompoundWanCacheMaxMs);
    public LatencyRangeMs CompoundStrategyDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.CompoundStrategyMinMs,
        BenchmarkDefaults.FaultTiming.CompoundStrategyMaxMs);
    public LatencyRangeMs TripleFaultWanCacheDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.TripleFaultWanCacheMinMs,
        BenchmarkDefaults.FaultTiming.TripleFaultWanCacheMaxMs);
    public LatencyRangeMs TripleFaultStrategyDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.TripleFaultStrategyMinMs,
        BenchmarkDefaults.FaultTiming.TripleFaultStrategyMaxMs);
    public LatencyRangeMs TripleFaultDbDelay { get; init; } = new(
        BenchmarkDefaults.FaultTiming.TripleFaultDbMinMs,
        BenchmarkDefaults.FaultTiming.TripleFaultDbMaxMs);
    public int IntermittentFailureModulo { get; init; } = BenchmarkDefaults.FaultTiming.IntermittentFailureModulo;
}

/// <summary>
/// Cấu hình hạ tầng thực thi của Benchmark Harness (ThreadPool, Timeout, Error Logging).
/// </summary>
public sealed class BenchmarkExecutionSettings
{
    public int MinWorkerThreads { get; init; } = BenchmarkDefaults.Execution.MinWorkerThreads;
    public int HttpClientTimeoutSeconds { get; init; } = BenchmarkDefaults.Execution.HttpClientTimeoutSeconds;
    public int MaxErrorLogEntriesPerRun { get; init; } = BenchmarkDefaults.Execution.MaxErrorLogEntriesPerRun;
}

public sealed class BenchmarkSuiteConfig
{
    public IReadOnlyList<ExperimentScenarioId> Scenarios { get; init; } = BenchmarkDefaults.DefaultScenarios;

    public IReadOnlyList<int> ConcurrencyLevels { get; init; } = BenchmarkDefaults.DefaultConcurrencyLevels;

    /// <summary>
    /// Số lượng requests đo đạc cho các kịch bản nhanh.
    /// </summary>
    public int FastScenarioRequests { get; init; } = BenchmarkDefaults.Execution.DefaultFastScenarioRequests;

    /// <summary>
    /// Số lượng requests đo đạc cho các kịch bản có tiêm độ trễ/timeout.
    /// </summary>
    public int FaultTimedScenarioRequests { get; init; } = BenchmarkDefaults.Execution.DefaultTimedScenarioRequests;

    public int WarmupRequests { get; init; } = BenchmarkDefaults.Execution.DefaultWarmupRequests;

    /// <summary>
    /// Chế độ Cache cho Layer A (G1/G2): "Memory" (Hermetic Controlled L1/L2 Baseline) hoặc "RedisCloud" (Live Redis Cloud WAN).
    /// </summary>
    public string CacheBackendMode { get; init; } = BenchmarkDefaults.CacheModes.Memory;

    public string OutputDirectory { get; init; } = BenchmarkDefaults.DefaultOutputDirectory;

    public BenchmarkResilienceSettings Resilience { get; init; } = new();

    public BenchmarkFaultTimingSettings FaultTiming { get; init; } = new();

    public BenchmarkExecutionSettings Execution { get; init; } = new();

    public int GetTotalRequestsFor(ExperimentScenarioId scenario) =>
        ScenarioCatalog.Get(scenario).IsTimedFaultScenario
            ? FaultTimedScenarioRequests
            : FastScenarioRequests;

    public int GetWarmupRequestsFor(ExperimentScenarioId scenario) =>
        ScenarioCatalog.Get(scenario).IsTimedFaultScenario
            ? Math.Min(WarmupRequests, FaultTiming.TimedScenarioMaxWarmupRequests)
            : WarmupRequests;
}

/// <summary>
/// Siêu dữ liệu truy vết cho từng lượt chạy thực nghiệm (Controlled Experimental Protocol Metadata).
/// </summary>
public sealed class RunMetadata
{
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("timestamp_utc")]
    public required string TimestampUtc { get; init; }

    [JsonPropertyName("scenario")]
    public required string Scenario { get; init; }

    [JsonPropertyName("experiment_layer")]
    public required string ExperimentLayer { get; init; }

    [JsonPropertyName("concurrency")]
    public required int Concurrency { get; init; }

    [JsonPropertyName("total_requests")]
    public required int TotalRequests { get; init; }

    [JsonPropertyName("warmup_requests")]
    public required int WarmupRequests { get; init; }

    [JsonPropertyName("strategy")]
    public required string Strategy { get; init; }

    [JsonPropertyName("strategy_version")]
    public required string StrategyVersion { get; init; }

    [JsonPropertyName("cache_state")]
    public required string CacheState { get; init; }

    [JsonPropertyName("redis_state")]
    public required string RedisState { get; init; }

    [JsonPropertyName("database_state")]
    public required string DatabaseState { get; init; }

    [JsonPropertyName("environment_profile")]
    public required string EnvironmentProfile { get; init; }

    [JsonPropertyName("machine_name")]
    public required string MachineName { get; init; }

    [JsonPropertyName("os_description")]
    public required string OsDescription { get; init; }

    [JsonPropertyName("cpu_cores")]
    public required int CpuCores { get; init; }

    [JsonPropertyName("dotnet_version")]
    public required string DotNetVersion { get; init; }

    [JsonPropertyName("outer_timeout_ms")]
    public required int OuterTimeoutMs { get; init; }

    [JsonPropertyName("inner_timeout_ms")]
    public required int InnerTimeoutMs { get; init; }

    [JsonPropertyName("retry_count")]
    public required int RetryCount { get; init; }

    [JsonPropertyName("retry_delay_ms")]
    public required int RetryDelayMs { get; init; }

    [JsonPropertyName("configured_resilience_budget_ms")]
    public required int ConfiguredResilienceBudgetMs { get; init; }

    [JsonPropertyName("circuit_state_setup")]
    public required string CircuitStateSetup { get; init; }
}

/// <summary>
/// Bản ghi đo lường thô của từng HTTP request trong pha Measurement,
/// tích hợp khóa kết nối 1:1 (DecisionId, ReservationId) và mã băm chuẩn hóa kỳ vọng (ExpectedRequestHash).
/// </summary>
public sealed class RawMeasurementRecord
{
    public required string RunId { get; init; }
    public required string Scenario { get; init; }
    public required int Concurrency { get; init; }
    public required int RequestIndex { get; init; }
    public Guid DecisionId { get; init; } = Guid.Empty;
    public Guid ReservationId { get; init; } = Guid.Empty;
    public string ExpectedRequestHash { get; init; } = string.Empty;
    public required double ClientElapsedMs { get; init; }
    public required int ServerExecutionTimeMs { get; init; }
    public required int HttpStatusCode { get; init; }
    public required string DecisionStatus { get; init; }
    public required string ExecutionType { get; init; }
    public required bool FallbackUsed { get; init; }
    public required bool CacheHit { get; init; }
    public required bool IsEligibleForDcr { get; init; }
    public required bool IsSafeCompletedDecision { get; init; }
    public string? FailureDetails { get; init; }
}

/// <summary>
/// Kết quả tổng hợp thống kê của một lượt chạy (Scenario x Concurrency).
/// </summary>
public sealed class ScenarioSummaryMetrics
{
    [JsonPropertyName("metadata")]
    public required RunMetadata Metadata { get; init; }

    [JsonPropertyName("total_wall_clock_ms")]
    public required double TotalWallClockMs { get; init; }

    [JsonPropertyName("throughput_rps")]
    public required double ThroughputRps { get; init; }

    // Client End-to-End HTTP Latency (ms)
    [JsonPropertyName("client_p50_ms")]
    public required double ClientP50Ms { get; init; }

    [JsonPropertyName("client_p90_ms")]
    public required double ClientP90Ms { get; init; }

    [JsonPropertyName("client_p95_ms")]
    public required double ClientP95Ms { get; init; }

    [JsonPropertyName("client_p99_ms")]
    public required double ClientP99Ms { get; init; }

    [JsonPropertyName("client_max_ms")]
    public required double ClientMaxMs { get; init; }

    [JsonPropertyName("client_mean_ms")]
    public required double ClientMeanMs { get; init; }

    [JsonPropertyName("client_stddev_ms")]
    public required double ClientStdDevMs { get; init; }

    // Server Internal Execution Latency (ms)
    [JsonPropertyName("server_p50_ms")]
    public required double ServerP50Ms { get; init; }

    [JsonPropertyName("server_p90_ms")]
    public required double ServerP90Ms { get; init; }

    [JsonPropertyName("server_p95_ms")]
    public required double ServerP95Ms { get; init; }

    [JsonPropertyName("server_p99_ms")]
    public required double ServerP99Ms { get; init; }

    [JsonPropertyName("server_max_ms")]
    public required double ServerMaxMs { get; init; }

    [JsonPropertyName("server_mean_ms")]
    public required double ServerMeanMs { get; init; }

    // Continuity, Cache & Resilience Telemetry
    [JsonPropertyName("eligible_requests")]
    public required int EligibleRequests { get; init; }

    [JsonPropertyName("completed_safe_decisions")]
    public required int CompletedSafeDecisions { get; init; }

    [JsonPropertyName("dcr_percent")]
    public required double DcrPercent { get; init; }

    [JsonPropertyName("cache_hit_ratio_percent")]
    public required double CacheHitRatioPercent { get; init; }

    [JsonPropertyName("fallback_rate_percent")]
    public required double FallbackRatePercent { get; init; }

    [JsonPropertyName("http_error_or_timeout_count")]
    public required int HttpErrorOrTimeoutCount { get; init; }

    [JsonPropertyName("http_error_rate_percent")]
    public required double HttpErrorRatePercent { get; init; }

    [JsonPropertyName("strategy_invocations")]
    public required int StrategyInvocations { get; init; }

    [JsonPropertyName("observed_retry_count")]
    public required int ObservedRetryCount { get; init; }

    [JsonPropertyName("observed_inner_timeout_count")]
    public required int ObservedInnerTimeoutCount { get; init; }

    [JsonPropertyName("circuit_state_observed")]
    public required string CircuitStateObserved { get; init; }

    // Layer A Differential Metrics (Computed when G0, G1, G2 are present at matching Concurrency)
    [JsonPropertyName("cache_miss_overhead_p50_ms")]
    public double? CacheMissOverheadP50Ms { get; set; }

    [JsonPropertyName("cache_miss_overhead_p95_ms")]
    public double? CacheMissOverheadP95Ms { get; set; }

    [JsonPropertyName("cache_benefit_p50_ms")]
    public double? CacheBenefitP50Ms { get; set; }

    [JsonPropertyName("cache_benefit_p95_ms")]
    public double? CacheBenefitP95Ms { get; set; }
}
