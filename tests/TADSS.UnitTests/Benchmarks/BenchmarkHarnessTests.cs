using FluentAssertions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using TADSS.BenchmarkRunner.Harness;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.BenchmarkRunner.Telemetry;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Strategies.Default;
using Xunit;

namespace TADSS.UnitTests.Benchmarks;

public sealed class BenchmarkHarnessTests
{
    #region 1. Statistical Functions (ResultAggregator)

    [Fact]
    public void ComputePercentile_WithFiveElements_ReturnsExactInterpolatedValues()
    {
        double[] sorted = [10.0, 20.0, 30.0, 40.0, 50.0];

        ResultAggregator.ComputePercentile(sorted, 0.00).Should().Be(10.0);
        ResultAggregator.ComputePercentile(sorted, 0.50).Should().Be(30.0);
        ResultAggregator.ComputePercentile(sorted, 0.75).Should().Be(40.0);
        ResultAggregator.ComputePercentile(sorted, 0.90).Should().BeApproximately(46.0, 0.001);
        ResultAggregator.ComputePercentile(sorted, 1.00).Should().Be(50.0);
    }

    [Fact]
    public void ComputePercentile_WithEmptyOrSingleElement_HandlesEdgeCasesCleanly()
    {
        ResultAggregator.ComputePercentile(ReadOnlySpan<double>.Empty, 0.50).Should().Be(0.0);
        ResultAggregator.ComputePercentile([42.5], 0.95).Should().Be(42.5);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ComputePercentile_WhenPercentileOutOfRange_ThrowsArgumentOutOfRangeException(double invalidPercentile)
    {
        double[] sorted = [1.0, 2.0, 3.0];
        var act = () => ResultAggregator.ComputePercentile(sorted, invalidPercentile);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ComputeMeanAndStdDev_ReturnsAccurateSampleStatistics()
    {
        double[] sample = [2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0];

        ResultAggregator.ComputeMean(sample).Should().Be(5.0);
        ResultAggregator.ComputeStdDev(sample).Should().BeApproximately(Math.Sqrt(32.0 / 7.0), 0.0001);
        ResultAggregator.ComputeStdDev([10.0]).Should().Be(0.0);
    }

    [Fact]
    public void Aggregate_WhenRecordsAreEmpty_ThrowsInvalidOperationException()
    {
        var metadata = CreateSampleMetadata(ExperimentScenarioId.G0, concurrency: 10);
        var act = () => ResultAggregator.Aggregate(metadata, [], 100.0, 0, 0, 0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Aggregate_ComputesDcrAndR2AndE4CircuitStatesCorrectly()
    {
        var r2Meta = CreateSampleMetadata(ExperimentScenarioId.R2, concurrency: 10);
        var r2Records = new List<RawMeasurementRecord>
        {
            CreateRecord("R2", 10, 0, clientMs: 1.5, serverMs: 0, status: 200, fallback: true, cacheHit: false, safe: true),
            CreateRecord("R2", 10, 1, clientMs: 2.5, serverMs: 0, status: 200, fallback: true, cacheHit: false, safe: true)
        };

        var r2Summary = ResultAggregator.Aggregate(r2Meta, r2Records, 10.0, 0, 0, 0);
        r2Summary.DcrPercent.Should().Be(100.0);
        r2Summary.CircuitStateObserved.Should().Be("OPEN (Verified 0 Strategy Invocations)");

        var e4Meta = CreateSampleMetadata(ExperimentScenarioId.E4, concurrency: 10);
        var e4Records = new List<RawMeasurementRecord>
        {
            CreateRecord("E4", 10, 0, clientMs: 2.0, serverMs: 1, status: 200, fallback: false, cacheHit: false, safe: true)
        };

        var e4Summary = ResultAggregator.Aggregate(e4Meta, e4Records, 10.0, strategyInvocations: 1, 0, 0);
        e4Summary.CircuitStateObserved.Should().Contain("RECOVERED_CLOSED");
    }

    [Fact]
    public void PopulateCacheDifferentials_ComputesMissOverheadAndHitBenefitAndWanOverhead()
    {
        var summaries = new List<ScenarioSummaryMetrics>
        {
            CreateSummary(ExperimentScenarioId.G0, concurrency: 10, p50: 2.00, p95: 4.00),
            CreateSummary(ExperimentScenarioId.G1, concurrency: 10, p50: 2.35, p95: 4.50),
            CreateSummary(ExperimentScenarioId.G2, concurrency: 10, p50: 0.80, p95: 1.50),
            CreateSummary(ExperimentScenarioId.C1, concurrency: 10, p50: 62.00, p95: 84.00)
        };

        ResultAggregator.PopulateCacheDifferentials(summaries);

        summaries[1].CacheMissOverheadP50Ms.Should().Be(0.35);
        summaries[1].CacheMissOverheadP95Ms.Should().Be(0.50);
        summaries[2].CacheBenefitP50Ms.Should().Be(1.20);
        summaries[2].CacheBenefitP95Ms.Should().Be(2.50);
        summaries[3].CacheMissOverheadP50Ms.Should().Be(60.00);
        summaries[3].CacheMissOverheadP95Ms.Should().Be(80.00);
    }

    #endregion

    #region 2. CLI Argument Parser & Validation

    [Fact]
    public void CliArgumentParser_WithNoArgs_ReturnsDefaultFull19Configuration()
    {
        var config = CliArgumentParser.ParseAndValidate([]);

        config.Scenarios.Should().HaveCount(19);
        config.ConcurrencyLevels.Should().Equal(1, 10, 50, 100);
        config.FastScenarioRequests.Should().Be(BenchmarkDefaults.Execution.DefaultFastScenarioRequests);
        config.FaultTimedScenarioRequests.Should().Be(BenchmarkDefaults.Execution.DefaultTimedScenarioRequests);
        config.Resilience.ComputedAttemptAndDelayBudgetMs.Should().Be(420);
    }

    [Fact]
    public void CliArgumentParser_WithCore7Suite_SelectsCoreSevenScenarios()
    {
        var config = CliArgumentParser.ParseAndValidate(["--suite", "Core7"]);
        config.Scenarios.Should().HaveCount(7);
        config.Scenarios.Should().Equal(BenchmarkDefaults.CoreSevenScenarios);
    }

    [Fact]
    public void CliArgumentParser_WithCustomValidArgs_OverridesConfigurationCleanly()
    {
        string[] args =
        [
            "--scenarios", "G0,C1,D0,E2,E4",
            "--vus", "5,25",
            "--fast-requests", "100",
            "--timed-requests", "50",
            "--warmup", "0",
            "--cache-mode", "RedisCloud",
            "--outer-timeout-ms", "600",
            "--inner-timeout-ms", "150",
            "--retry-count", "2",
            "--retry-delay-ms", "40"
        ];

        var config = CliArgumentParser.ParseAndValidate(args);

        config.Scenarios.Should().Equal(
            ExperimentScenarioId.G0,
            ExperimentScenarioId.C1,
            ExperimentScenarioId.D0,
            ExperimentScenarioId.E2,
            ExperimentScenarioId.E4);
        config.ConcurrencyLevels.Should().Equal(5, 25);
        config.FastScenarioRequests.Should().Be(100);
        config.FaultTimedScenarioRequests.Should().Be(50);
        config.WarmupRequests.Should().Be(0);
        config.CacheBackendMode.Should().Be("RedisCloud");
        config.Resilience.OuterTimeoutMs.Should().Be(600);
        config.Resilience.ComputedAttemptAndDelayBudgetMs.Should().Be(3 * 150 + 2 * 40);
    }

    [Theory]
    [InlineData("--fast-requests", "0")]
    [InlineData("--fast-requests", "-10")]
    [InlineData("--timed-requests", "0")]
    [InlineData("--warmup", "-1")]
    [InlineData("--vus", "10,-5")]
    [InlineData("--scenarios", "G0,INVALID_SCENARIO")]
    [InlineData("--suite", "UnknownSuite")]
    [InlineData("--cache-mode", "InvalidBackend")]
    [InlineData("--unknown-flag", "123")]
    public void CliArgumentParser_WithInvalidArguments_ThrowsArgumentException(string flag, string value)
    {
        var act = () => CliArgumentParser.ParseAndValidate([flag, value]);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region 3. All 19 Scenarios in ScenarioCatalog & ScenarioConfigurationFactory

    [Theory]
    [InlineData(ExperimentScenarioId.G0, true, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.G1, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.G2, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.R0, true, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.R1, true, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.R2, true, CircuitBreakerBenchmarkMode.PreOpenedFastFail)]
    [InlineData(ExperimentScenarioId.R3, true, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.C0, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.C1, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.C2, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.C3, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.D0, true, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.D1, true, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.D2, true, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.E0, false, CircuitBreakerBenchmarkMode.StandardClosed)]
    [InlineData(ExperimentScenarioId.E1, false, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.E2, false, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.E3, true, CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)]
    [InlineData(ExperimentScenarioId.E4, true, CircuitBreakerBenchmarkMode.HalfOpenRecoveryTransition)]
    public void ScenarioConfigurationFactory_CreatesExpectedProfileForEveryScenario(
        ExperimentScenarioId scenarioId,
        bool expectedDisableCache,
        CircuitBreakerBenchmarkMode expectedCircuitMode)
    {
        var suiteConfig = new BenchmarkSuiteConfig();
        var config = ScenarioConfigurationFactory.Create(scenarioId, suiteConfig);

        config.Descriptor.Id.Should().Be(scenarioId);
        config.Descriptor.DisableCache.Should().Be(expectedDisableCache);
        config.Descriptor.CircuitMode.Should().Be(expectedCircuitMode);

        var resilienceOpts = new ResilienceOptions();
        config.ConfigureResilience(resilienceOpts);

        resilienceOpts.OuterTimeoutMs.Should().Be(BenchmarkDefaults.Resilience.OuterTimeoutMs);
        resilienceOpts.InnerTimeoutMs.Should().Be(BenchmarkDefaults.Resilience.InnerTimeoutMs);

        if (expectedCircuitMode == CircuitBreakerBenchmarkMode.PreOpenedFastFail)
        {
            resilienceOpts.CircuitBreakerBreakDurationSeconds.Should().Be(BenchmarkDefaults.Resilience.PreOpenedBreakDurationSeconds);
        }
        else if (expectedCircuitMode == CircuitBreakerBenchmarkMode.HalfOpenRecoveryTransition)
        {
            resilienceOpts.CircuitBreakerBreakDurationSeconds.Should().Be(BenchmarkDefaults.Resilience.HalfOpenRecoveryBreakDurationSeconds);
        }
        else if (expectedCircuitMode == CircuitBreakerBenchmarkMode.IsolatedClosedNoShortCircuit)
        {
            resilienceOpts.CircuitBreakerMinimumThroughput.Should().Be(int.MaxValue);
        }
    }

    #endregion

    #region 4. New Layer C, D, E Decorators & Test Doubles

    [Fact]
    public async Task BoundedCapacityCacheService_WhenCapacityExceeded_EvictsLeastRecentlyUsedEntry()
    {
        var cache = new BoundedCapacityCacheService(maxCapacity: 2);
        var dto1 = CreateDummyDecisionResult();
        var dto2 = CreateDummyDecisionResult();
        var dto3 = CreateDummyDecisionResult();

        await cache.SetAsync("key1", dto1);
        await cache.SetAsync("key2", dto2);

        // Touch key1 so key2 becomes LRU
        (await cache.GetAsync("key1")).Should().NotBeNull();

        // Insert key3 -> should evict key2
        await cache.SetAsync("key3", dto3);

        (await cache.GetAsync("key2")).Should().BeNull();
        (await cache.GetAsync("key1")).Should().NotBeNull();
        (await cache.GetAsync("key3")).Should().NotBeNull();
    }

    [Fact]
    public async Task IntermittentFaultBenchmarkStrategy_FailsOnAttemptOneAndRecoversOnAttemptTwo()
    {
        var strategy = new IntermittentFaultBenchmarkStrategy(new DefaultDecisionStrategy(), failureModulo: 2);
        var res1 = CreateDummyReservation();
        var res2 = CreateDummyReservation();

        // Reservation #1 (seq=1): 1 % 2 != 0 -> succeeds on Attempt 1
        var result1 = await strategy.EvaluateAsync(res1);
        strategy.WasDecisionEvaluatedByStrategy(result1.DecisionId).Should().BeTrue();

        // Reservation #2 (seq=2): 2 % 2 == 0 -> throws on Attempt 1, succeeds on Attempt 2
        var actAttempt1 = async () => await strategy.EvaluateAsync(res2);
        await actAttempt1.Should().ThrowAsync<InvalidOperationException>();

        var result2Attempt2 = await strategy.EvaluateAsync(res2);
        strategy.WasDecisionEvaluatedByStrategy(result2Attempt2.DecisionId).Should().BeTrue();
        strategy.RetryInvocations.Should().Be(1);
    }

    [Fact]
    public void MeasurementWorker_ClassifyException_MapsToSpecificDiagnosticCode()
    {
        MeasurementWorker.ClassifyException(new TaskCanceledException()).Should().Be("CLIENT_TIMEOUT");
        MeasurementWorker.ClassifyException(new HttpRequestException("Conn refused")).Should().Be("NETWORK_ERROR");
        MeasurementWorker.ClassifyException(new TimeoutException()).Should().Be("OPERATION_TIMEOUT");
        MeasurementWorker.ClassifyException(new InvalidOperationException()).Should().Be("UNHANDLED_EXCEPTION");
    }

    #endregion

    #region Helpers

    private static Reservation CreateDummyReservation()
    {
        var start = DateTime.UtcNow.AddDays(1);
        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "VIP",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: start,
            endTime: start.AddHours(2),
            totalPrice: 200000m,
            depositAmount: 200000m,
            paymentStatus: PaymentStatus.PAID,
            bookingType: "SINGLE",
            priority: BookingPriority.HIGH,
            requestedAt: start.AddHours(-24));
    }

    private static DecisionResultDto CreateDummyDecisionResult() => new()
    {
        DecisionId = Guid.NewGuid(),
        ReservationId = Guid.NewGuid(),
        Status = DecisionStatus.APPROVED.ToString(),
        StrategyName = "DefaultDecisionStrategy",
        StrategyVersion = "1.0.0",
        ExecutionTimeMs = 1,
        FallbackUsed = false,
        PrimaryReason = "TEST",
        Reasons = [],
        Recommendations = [],
        EvaluatedAt = DateTime.UtcNow
    };

    private static RunMetadata CreateSampleMetadata(ExperimentScenarioId scenario, int concurrency)
    {
        var suiteConfig = new BenchmarkSuiteConfig();
        var scenarioConfig = ScenarioConfigurationFactory.Create(scenario, suiteConfig);
        return ScenarioConfigurationFactory.BuildRunMetadata(scenarioConfig, suiteConfig, concurrency, 100, 10);
    }

    private static RawMeasurementRecord CreateRecord(
        string scenario,
        int concurrency,
        int index,
        double clientMs,
        int serverMs,
        int status,
        bool fallback,
        bool cacheHit,
        bool safe) => new()
    {
        RunId = $"TEST-{scenario}",
        Scenario = scenario,
        Concurrency = concurrency,
        RequestIndex = index,
        ClientElapsedMs = clientMs,
        ServerExecutionTimeMs = serverMs,
        HttpStatusCode = status,
        DecisionStatus = "APPROVED",
        ExecutionType = fallback ? "FALLBACK" : "AUTOMATED",
        FallbackUsed = fallback,
        CacheHit = cacheHit,
        IsEligibleForDcr = true,
        IsSafeCompletedDecision = safe
    };

    private static ScenarioSummaryMetrics CreateSummary(
        ExperimentScenarioId scenario,
        int concurrency,
        double p50,
        double p95) => new()
    {
        Metadata = CreateSampleMetadata(scenario, concurrency),
        TotalWallClockMs = 100,
        ThroughputRps = 1000,
        ClientP50Ms = p50,
        ClientP90Ms = p95,
        ClientP95Ms = p95,
        ClientP99Ms = p95,
        ClientMaxMs = p95,
        ClientMeanMs = p50,
        ClientStdDevMs = 0.1,
        ServerP50Ms = 1,
        ServerP90Ms = 1,
        ServerP95Ms = 1,
        ServerP99Ms = 1,
        ServerMaxMs = 1,
        ServerMeanMs = 1,
        EligibleRequests = 100,
        CompletedSafeDecisions = 100,
        DcrPercent = 100,
        CacheHitRatioPercent = scenario == ExperimentScenarioId.G2 ? 100 : 0,
        FallbackRatePercent = 0,
        HttpErrorOrTimeoutCount = 0,
        HttpErrorRatePercent = 0,
        StrategyInvocations = 100,
        ObservedRetryCount = 0,
        ObservedInnerTimeoutCount = 0,
        CircuitStateObserved = "CLOSED"
    };

    #endregion
}
