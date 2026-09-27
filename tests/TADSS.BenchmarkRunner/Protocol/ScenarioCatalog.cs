namespace TADSS.BenchmarkRunner.Protocol;

public enum CircuitBreakerBenchmarkMode
{
    StandardClosed,
    IsolatedClosedNoShortCircuit,
    PreOpenedFastFail,
    HalfOpenRecoveryTransition
}

public enum PayloadDistributionMode
{
    /// <summary>
    /// Mỗi request mang một ReservationId riêng biệt (G0, G1, R0..R3, C1, D0..D2, E2..E4).
    /// </summary>
    UniquePerRequest,

    /// <summary>
    /// Toàn bộ request dùng chung 1 canonical payload đã pre-warm (G2) hoặc chưa pre-warm (C2 Thundering Herd).
    /// </summary>
    IdenticalCanonicalKey,

    /// <summary>
    /// 50% request khớp key đã pre-warm (Cache Hit), 50% request mang key mới (Cache Miss) (C0, E1).
    /// </summary>
    Mixed50PercentHit,

    /// <summary>
    /// 80% request khớp key đã pre-warm (Cache Hit), 20% request mang key mới (Cache Miss) (E0).
    /// </summary>
    Mixed80PercentHit,

    /// <summary>
    /// Chu kỳ 128 khóa trên bộ đệm giới hạn 32 phần tử để tạo áp lực trục xuất LRU/FIFO (C3).
    /// </summary>
    BoundedWorkingSetCycle
}

/// <summary>
/// Siêu dữ liệu định nghĩa đặc tính của một kịch bản thực nghiệm.
/// </summary>
public sealed record ScenarioDescriptor(
    ExperimentScenarioId Id,
    string ExperimentLayer,
    string Description,
    string CacheStateLabel,
    string CircuitSetupLabel,
    bool DisableCache,
    bool PreWarmCache,
    bool IsTimedFaultScenario,
    CircuitBreakerBenchmarkMode CircuitMode,
    PayloadDistributionMode PayloadMode = PayloadDistributionMode.UniquePerRequest);

/// <summary>
/// Danh mục khai báo tập trung (Declarative Catalog) cho toàn bộ 19 kịch bản thực nghiệm (Layers A, B, C, D, E).
/// </summary>
public static class ScenarioCatalog
{
    private static readonly IReadOnlyDictionary<ExperimentScenarioId, ScenarioDescriptor> Registry =
        new Dictionary<ExperimentScenarioId, ScenarioDescriptor>
        {
            // =========================================================================
            // LAYER A: CACHE BASELINE (G0 - G2)
            // =========================================================================
            [ExperimentScenarioId.G0] = new(
                Id: ExperimentScenarioId.G0,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerACache,
                Description: "No Cache (Direct Execution Baseline)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.G1] = new(
                Id: ExperimentScenarioId.G1,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerACache,
                Description: "Cache Miss Path (RFC 8785 + SHA-256 + Set)",
                CacheStateLabel: "enabled_miss_path",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.G2] = new(
                Id: ExperimentScenarioId.G2,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerACache,
                Description: "Cache Hit Path (Pre-warmed O(1) Lookup)",
                CacheStateLabel: "enabled_prewarmed_hit_path",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: true,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.IdenticalCanonicalKey),

            // =========================================================================
            // LAYER B: RESILIENCE & FAULT ISOLATION (R0 - R3)
            // =========================================================================
            [ExperimentScenarioId.R0] = new(
                Id: ExperimentScenarioId.R0,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerBResilience,
                Description: "Normal Strategy Execution",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.R1] = new(
                Id: ExperimentScenarioId.R1,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerBResilience,
                Description: "Retry Recovery (Attempt 1 Timeout ➔ Attempt 2 OK)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.R2] = new(
                Id: ExperimentScenarioId.R2,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerBResilience,
                Description: "Circuit Breaker OPEN (Fast-Fail ➔ Fallback)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "PRE_OPENED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.PreOpenedFastFail,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.R3] = new(
                Id: ExperimentScenarioId.R3,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerBResilience,
                Description: "Retry Exhaustion (3 Attempts Timeout ➔ Fallback)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED_NO_SHORTCIRCUIT",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            // =========================================================================
            // LAYER C: REAL-WORLD CACHE PATHOLOGIES (C0 - C3)
            // =========================================================================
            [ExperimentScenarioId.C0] = new(
                Id: ExperimentScenarioId.C0,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerCCachePathologies,
                Description: "Partial Cache Hit (50% Warm Hit / 50% Cold Miss)",
                CacheStateLabel: "partial_50_percent_warm",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: true,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.Mixed50PercentHit),

            [ExperimentScenarioId.C1] = new(
                Id: ExperimentScenarioId.C1,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerCCachePathologies,
                Description: "Slow Redis WAN Latency (25-45ms Network RTT per Cache Op)",
                CacheStateLabel: "wan_high_latency_cache",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.C2] = new(
                Id: ExperimentScenarioId.C2,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerCCachePathologies,
                Description: "Cache Thundering Herd (Concurrent Cold Misses on Same Key)",
                CacheStateLabel: "cold_stampede_identical_key",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.IdenticalCanonicalKey),

            [ExperimentScenarioId.C3] = new(
                Id: ExperimentScenarioId.C3,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerCCachePathologies,
                Description: "Cache Eviction Under Pressure (Bounded Capacity 32 / 128 Keys)",
                CacheStateLabel: "bounded_capacity_eviction",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: true,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.BoundedWorkingSetCycle),

            // =========================================================================
            // LAYER D: STRATEGY & PERSISTENCE PERFORMANCE (D0 - D2)
            // =========================================================================
            [ExperimentScenarioId.D0] = new(
                Id: ExperimentScenarioId.D0,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerDStrategyPersistence,
                Description: "Heavy Multi-Rule Strategy Near Timeout Edge (80-105ms)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.D1] = new(
                Id: ExperimentScenarioId.D1,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerDStrategyPersistence,
                Description: "Database Write Contention (Slow Audit Persistence 20-40ms)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.D2] = new(
                Id: ExperimentScenarioId.D2,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerDStrategyPersistence,
                Description: "Bimodal Strategy Jitter (80% Fast 5ms / 20% Slow 95ms)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            // =========================================================================
            // LAYER E: COMBINED PRODUCTION STRESS & SELF-HEALING (E0 - E4)
            // =========================================================================
            [ExperimentScenarioId.E0] = new(
                Id: ExperimentScenarioId.E0,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerECombinedStress,
                Description: "Composite Production Baseline (80% Cache Hit + 20% Strategy/DB)",
                CacheStateLabel: "composite_80_percent_hit",
                CircuitSetupLabel: "CLOSED",
                DisableCache: false,
                PreWarmCache: true,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.StandardClosed,
                PayloadMode: PayloadDistributionMode.Mixed80PercentHit),

            [ExperimentScenarioId.E1] = new(
                Id: ExperimentScenarioId.E1,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerECombinedStress,
                Description: "Compound WAN Cache (15-25ms) + Heavy Strategy (65-85ms)",
                CacheStateLabel: "wan_cache_50_percent_hit",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: false,
                PreWarmCache: true,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.Mixed50PercentHit),

            [ExperimentScenarioId.E2] = new(
                Id: ExperimentScenarioId.E2,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerECombinedStress,
                Description: "Cascading Triple Stress (WAN Cache + Heavy Strategy + Slow DB)",
                CacheStateLabel: "wan_cache_miss_stress",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: false,
                PreWarmCache: false,
                IsTimedFaultScenario: true,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.E3] = new(
                Id: ExperimentScenarioId.E3,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerECombinedStress,
                Description: "Intermittent Transient Faults (~14% Attempt 1 Fail ➔ Retry OK)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "CLOSED_ISOLATED",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit,
                PayloadMode: PayloadDistributionMode.UniquePerRequest),

            [ExperimentScenarioId.E4] = new(
                Id: ExperimentScenarioId.E4,
                ExperimentLayer: BenchmarkDefaults.Layers.LayerECombinedStress,
                Description: "Circuit Breaker Recovery (OPEN ➔ Half-Open Probe ➔ CLOSED)",
                CacheStateLabel: "disabled_noop",
                CircuitSetupLabel: "HALF_OPEN_RECOVERY",
                DisableCache: true,
                PreWarmCache: false,
                IsTimedFaultScenario: false,
                CircuitMode: CircuitBreakerBenchmarkMode.HalfOpenRecoveryTransition,
                PayloadMode: PayloadDistributionMode.UniquePerRequest)
        };

    public static IReadOnlyList<ScenarioDescriptor> All { get; } = Registry.Values.ToList();

    public static ScenarioDescriptor Get(ExperimentScenarioId scenarioId)
    {
        if (!Registry.TryGetValue(scenarioId, out var descriptor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scenarioId),
                scenarioId,
                $"Unregistered experiment scenario '{scenarioId}'.");
        }

        return descriptor;
    }
}
