using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Strategies.Default;

namespace TADSS.BenchmarkRunner.Harness;

public sealed class ScenarioConfiguration
{
    public required ScenarioDescriptor Descriptor { get; init; }
    public required InstrumentedBenchmarkStrategyBase Strategy { get; init; }
    public required IBenchmarkResettableCache CustomCacheService { get; init; }
    public SimulatedDbContentionInterceptor? DbContentionInterceptor { get; init; }
    public required bool UseLiveRedisCloud { get; init; }
    public required string RedisStateLabel { get; init; }
    public required string DatabaseStateLabel { get; init; }
    public required string EnvironmentProfileLabel { get; init; }
    public required Action<ResilienceOptions> ConfigureResilience { get; init; }

    public void ActivateMeasurementPhaseFaults()
    {
        if (CustomCacheService is NetworkLatencyCacheDecorator wanCache)
        {
            wanCache.EnableMeasurementLatency(true);
        }

        DbContentionInterceptor?.EnableMeasurementContention(true);

        if (Strategy is RecoveringCircuitBenchmarkStrategy recoveringStrategy)
        {
            recoveringStrategy.EnterHealthyRecoveryPhase();
        }
    }
}

/// <summary>
/// Factory tạo cấu hình thực nghiệm hoàn chỉnh cho toàn bộ 19 kịch bản (G0..G2, R0..R3, C0..C3, D0..D2, E0..E4),
/// tách biệt hoàn toàn logic thiết lập kịch bản khỏi vòng đời WebApplicationFactory.
/// </summary>
public static class ScenarioConfigurationFactory
{
    private static readonly IReadOnlyDictionary<ExperimentScenarioId, Func<BenchmarkSuiteConfig, InstrumentedBenchmarkStrategyBase>> StrategyFactories =
        new Dictionary<ExperimentScenarioId, Func<BenchmarkSuiteConfig, InstrumentedBenchmarkStrategyBase>>
        {
            // Layer A: Cache Baseline
            [ExperimentScenarioId.G0] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.G1] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.G2] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),

            // Layer B: Resilience
            [ExperimentScenarioId.R0] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.R1] = cfg => new RecoverableTimeoutBenchmarkStrategy(cfg.FaultTiming.RecoverableFirstAttemptDelayMs),
            [ExperimentScenarioId.R2] = cfg => new ExhaustiveTimeoutBenchmarkStrategy(cfg.FaultTiming.ExhaustiveAttemptDelayMs),
            [ExperimentScenarioId.R3] = cfg => new ExhaustiveTimeoutBenchmarkStrategy(cfg.FaultTiming.ExhaustiveAttemptDelayMs),

            // Layer C: Cache Pathologies
            [ExperimentScenarioId.C0] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.C1] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.C2] = cfg => new VariableDelayBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.ThunderingHerdColdComputeDelay),
            [ExperimentScenarioId.C3] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),

            // Layer D: Strategy & Persistence Performance
            [ExperimentScenarioId.D0] = cfg => new VariableDelayBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.HeavyStrategyDelay),
            [ExperimentScenarioId.D1] = _ => new InstrumentedNormalStrategy(new DefaultDecisionStrategy()),
            [ExperimentScenarioId.D2] = cfg => new BimodalJitterBenchmarkStrategy(
                new DefaultDecisionStrategy(),
                cfg.FaultTiming.JitterFastDelayMs,
                cfg.FaultTiming.JitterSlowDelayMs,
                cfg.FaultTiming.JitterSlowRequestModulo),

            // Layer E: Combined Stress & Self-Healing
            [ExperimentScenarioId.E0] = cfg => new VariableDelayBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.CompositeFastStrategyDelay),
            [ExperimentScenarioId.E1] = cfg => new VariableDelayBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.CompoundStrategyDelay),
            [ExperimentScenarioId.E2] = cfg => new VariableDelayBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.TripleFaultStrategyDelay),
            [ExperimentScenarioId.E3] = cfg => new IntermittentFaultBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.IntermittentFailureModulo),
            [ExperimentScenarioId.E4] = cfg => new RecoveringCircuitBenchmarkStrategy(new DefaultDecisionStrategy(), cfg.FaultTiming.ExhaustiveAttemptDelayMs)
        };

    public static ScenarioConfiguration Create(ExperimentScenarioId scenarioId, BenchmarkSuiteConfig suiteConfig)
    {
        var descriptor = ScenarioCatalog.Get(scenarioId);
        if (!StrategyFactories.TryGetValue(scenarioId, out var strategyFactory))
        {
            throw new ArgumentOutOfRangeException(nameof(scenarioId), scenarioId, $"No strategy factory registered for '{scenarioId}'.");
        }

        bool isRedisCloudRequested = suiteConfig.CacheBackendMode.Equals(
            BenchmarkDefaults.CacheModes.RedisCloud,
            StringComparison.OrdinalIgnoreCase);

        bool useLiveRedisCloud = isRedisCloudRequested
            && scenarioId is ExperimentScenarioId.G1 or ExperimentScenarioId.G2;

        var customCache = CreateCustomCacheService(scenarioId, descriptor, suiteConfig.FaultTiming);
        var dbContentionInterceptor = CreateDbContentionInterceptor(scenarioId, suiteConfig.FaultTiming);

        string redisStateLabel = ResolveRedisStateLabel(scenarioId, descriptor, isRedisCloudRequested);
        string databaseStateLabel = dbContentionInterceptor != null
            ? "simulated_write_contention"
            : "seeded_isolated_per_run";

        string environmentProfile = useLiveRedisCloud
            ? "CloudManaged_RedisCloud"
            : "ControlledLaboratoryHarness";

        var strategy = strategyFactory(suiteConfig);
        var resilienceConfig = suiteConfig.Resilience;

        return new ScenarioConfiguration
        {
            Descriptor = descriptor,
            Strategy = strategy,
            CustomCacheService = customCache,
            DbContentionInterceptor = dbContentionInterceptor,
            UseLiveRedisCloud = useLiveRedisCloud,
            RedisStateLabel = redisStateLabel,
            DatabaseStateLabel = databaseStateLabel,
            EnvironmentProfileLabel = environmentProfile,
            ConfigureResilience = opts => ApplyResilienceProfile(opts, descriptor.CircuitMode, resilienceConfig)
        };
    }

    public static RunMetadata BuildRunMetadata(
        ScenarioConfiguration scenarioConfig,
        BenchmarkSuiteConfig suiteConfig,
        int concurrency,
        int totalRequests,
        int warmupRequests)
    {
        var timestamp = DateTime.UtcNow;
        var descriptor = scenarioConfig.Descriptor;
        var resil = suiteConfig.Resilience;

        return new RunMetadata
        {
            RunId = $"EXP-W8-{timestamp:yyyyMMdd}-{descriptor.Id}-VU{concurrency:D3}",
            TimestampUtc = timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Scenario = descriptor.Id.ToString(),
            ExperimentLayer = descriptor.ExperimentLayer,
            Concurrency = concurrency,
            TotalRequests = totalRequests,
            WarmupRequests = warmupRequests,
            Strategy = scenarioConfig.Strategy.StrategyName,
            StrategyVersion = scenarioConfig.Strategy.StrategyVersion,
            CacheState = descriptor.CacheStateLabel,
            RedisState = scenarioConfig.RedisStateLabel,
            DatabaseState = scenarioConfig.DatabaseStateLabel,
            EnvironmentProfile = scenarioConfig.EnvironmentProfileLabel,
            MachineName = Environment.MachineName,
            OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            CpuCores = Environment.ProcessorCount,
            DotNetVersion = Environment.Version.ToString(),
            OuterTimeoutMs = resil.OuterTimeoutMs,
            InnerTimeoutMs = resil.InnerTimeoutMs,
            RetryCount = resil.RetryCount,
            RetryDelayMs = resil.RetryDelayMs,
            ConfiguredResilienceBudgetMs = resil.ComputedAttemptAndDelayBudgetMs,
            CircuitStateSetup = descriptor.CircuitSetupLabel
        };
    }

    private static IBenchmarkResettableCache CreateCustomCacheService(
        ExperimentScenarioId scenarioId,
        ScenarioDescriptor descriptor,
        BenchmarkFaultTimingSettings faultTiming)
    {
        if (descriptor.DisableCache)
        {
            return new NoOpDecisionCacheService();
        }

        return scenarioId switch
        {
            ExperimentScenarioId.C1 => new NetworkLatencyCacheDecorator(
                new SerializedInProcessCacheService(),
                faultTiming.WanCacheLatency),

            ExperimentScenarioId.C3 => new BoundedCapacityCacheService(
                faultTiming.BoundedCacheCapacity),

            ExperimentScenarioId.E1 => new NetworkLatencyCacheDecorator(
                new SerializedInProcessCacheService(),
                faultTiming.CompoundWanCacheDelay),

            ExperimentScenarioId.E2 => new NetworkLatencyCacheDecorator(
                new SerializedInProcessCacheService(),
                faultTiming.TripleFaultWanCacheDelay),

            _ => new SerializedInProcessCacheService()
        };
    }

    private static SimulatedDbContentionInterceptor? CreateDbContentionInterceptor(
        ExperimentScenarioId scenarioId,
        BenchmarkFaultTimingSettings faultTiming) => scenarioId switch
    {
        ExperimentScenarioId.D1 => new SimulatedDbContentionInterceptor(faultTiming.DbContentionDelay),
        ExperimentScenarioId.E0 => new SimulatedDbContentionInterceptor(faultTiming.CompositeFastDbDelay),
        ExperimentScenarioId.E2 => new SimulatedDbContentionInterceptor(faultTiming.TripleFaultDbDelay),
        _ => null
    };

    private static string ResolveRedisStateLabel(
        ExperimentScenarioId scenarioId,
        ScenarioDescriptor descriptor,
        bool isRedisCloudRequested)
    {
        if (descriptor.DisableCache) return "bypassed";

        return scenarioId switch
        {
            ExperimentScenarioId.C1 or ExperimentScenarioId.E1 or ExperimentScenarioId.E2 => "simulated_wan_latency",
            ExperimentScenarioId.C3 => "bounded_lru_capacity_32",
            _ => isRedisCloudRequested ? "available_redis_cloud" : "isolated_l1_l2_harness"
        };
    }

    private static void ApplyResilienceProfile(
        ResilienceOptions opts,
        CircuitBreakerBenchmarkMode circuitMode,
        BenchmarkResilienceSettings resil)
    {
        opts.OuterTimeoutMs = resil.OuterTimeoutMs;
        opts.InnerTimeoutMs = resil.InnerTimeoutMs;
        opts.MaxRetryAttempts = resil.RetryCount;
        opts.RetryBaseDelayMs = resil.RetryDelayMs;
        opts.CircuitBreakerSamplingSeconds = resil.SamplingDurationSeconds;

        switch (circuitMode)
        {
            case CircuitBreakerBenchmarkMode.PreOpenedFastFail:
                opts.CircuitBreakerMinimumThroughput = resil.PreOpenedMinimumThroughput;
                opts.CircuitBreakerFailureRatio = resil.StandardFailureRatio;
                opts.CircuitBreakerBreakDurationSeconds = resil.PreOpenedBreakDurationSeconds;
                break;

            case CircuitBreakerBenchmarkMode.HalfOpenRecoveryTransition:
                opts.CircuitBreakerMinimumThroughput = resil.PreOpenedMinimumThroughput;
                opts.CircuitBreakerFailureRatio = resil.StandardFailureRatio;
                opts.CircuitBreakerBreakDurationSeconds = resil.HalfOpenRecoveryBreakDurationSeconds;
                break;

            case CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit:
                opts.CircuitBreakerMinimumThroughput = BenchmarkDefaults.Resilience.IsolatedNoBreakMinimumThroughput;
                opts.CircuitBreakerFailureRatio = BenchmarkDefaults.Resilience.IsolatedNoBreakFailureRatio;
                opts.CircuitBreakerBreakDurationSeconds = resil.StandardBreakDurationSeconds;
                break;

            default:
                opts.CircuitBreakerMinimumThroughput = resil.StandardMinimumThroughput;
                opts.CircuitBreakerFailureRatio = resil.StandardFailureRatio;
                opts.CircuitBreakerBreakDurationSeconds = resil.StandardBreakDurationSeconds;
                break;
        }
    }
}
