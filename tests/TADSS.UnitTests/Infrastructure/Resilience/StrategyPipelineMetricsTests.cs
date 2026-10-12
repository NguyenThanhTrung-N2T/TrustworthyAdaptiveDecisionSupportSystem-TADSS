using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// UT-07 / OB-5 (phần metric của pipeline): <see cref="StrategyPipeline"/> ghi <c>tadss.pipeline.duration</c> (<c>automated</c>, <c>fallback</c>, <c>timeout</c>),
/// <c>tadss.fallbacks</c> (đúng một lần cho mỗi quyết định fallback, kèm lý do và strategy) và gauge <c>tadss.circuit.state</c> theo từng strategy.
/// Hủy của caller không ghi số đo nào. Mỗi test có <see cref="TadssMetrics"/> riêng để số đo không lẫn nhau.
/// </summary>
public class StrategyPipelineMetricsTests
{
    private const string StrategyName = "MetricStrategy";

    private static ResilienceOptions Options(
        int maxRetryAttempts = 0,
        int innerTimeoutMs = 2_000,
        int outerTimeoutMs = 5_000,
        int minimumThroughput = 10,
        int breakSeconds = 60) => new()
        {
            OuterTimeoutMs = outerTimeoutMs,
            InnerTimeoutMs = innerTimeoutMs,
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMs = 1,
            CircuitBreakerMinimumThroughput = minimumThroughput,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerBreakDurationSeconds = breakSeconds,
        };

    private static StrategyDecision Approved() =>
        StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng thành viên."]);

    private static Task<StrategyDecision> Throwing() =>
        Task.FromException<StrategyDecision>(new InvalidOperationException("lỗi giả"));

    private static async Task<StrategyDecision> WaitForever(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("Không được tới đây.");
    }

    private static Task RunAsync(StrategyPipeline pipeline, DelegateStrategy strategy, CancellationToken token = default) =>
        pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), token);

    /// <summary>Strategy thành công ghi một thời lượng <c>automated</c> và không có fallback; strategy lỗi, quá hạn hoặc breaker mở ghi <c>fallback</c> kèm đúng lý do.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [Trait("CatalogId", "UT-07")]
    [InlineData("success", "automated", null)]
    [InlineData("throws", "fallback", "exception")]
    [InlineData("inner-timeout", "fallback", "timeout")]
    [InlineData("circuit-open", "fallback", "circuit_open")]
    public async Task ExecuteAsync_Outcome_RecordsDurationAndFallback_Theory(string scenario, string expectedOutcome, string? expectedReason)
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var options = scenario == "circuit-open" ? Options(minimumThroughput: 2) : Options(innerTimeoutMs: scenario == "inner-timeout" ? 30 : 2_000);
        using var pipeline = new StrategyPipeline(MsOptions.Create(options), TimeProvider.System, metrics, NullLogger<StrategyPipeline>.Instance);
        var strategy = scenario switch
        {
            "success" => new DelegateStrategy((_, _, _) => Task.FromResult(Approved()), strategyName: StrategyName),
            "inner-timeout" => new DelegateStrategy((_, _, token) => WaitForever(token), strategyName: StrategyName),
            _ => new DelegateStrategy((_, _, _) => Throwing(), strategyName: StrategyName),
        };

        if (scenario == "circuit-open")
        {
            // Hai lỗi đủ ngưỡng thông lượng làm breaker mở; lượt thứ ba mới là fast-fail được kiểm tra
            await RunAsync(pipeline, strategy);
            await RunAsync(pipeline, strategy);
            recorder.Sum(TadssMetricNames.Fallbacks, ("reason", "circuit_open")).Should().Be(0, "hai lượt đầu là lỗi strategy, chưa phải fast-fail");
        }

        await RunAsync(pipeline, strategy);

        var expectedRuns = scenario == "circuit-open" ? 3 : 1;
        recorder.Count(TadssMetricNames.PipelineDuration, ("strategy", StrategyName)).Should().Be(expectedRuns);
        recorder.Count(TadssMetricNames.PipelineDuration, ("strategy", StrategyName), ("outcome", expectedOutcome)).Should().BeGreaterThanOrEqualTo(1);
        recorder.Values(TadssMetricNames.PipelineDuration, ("strategy", StrategyName)).Should().OnlyContain(value => value >= 0);
        if (expectedReason is null)
        {
            recorder.Sum(TadssMetricNames.Fallbacks).Should().Be(0);
        }
        else
        {
            recorder.Sum(TadssMetricNames.Fallbacks, ("reason", expectedReason), ("strategy", StrategyName)).Should().Be(1, "mỗi quyết định fallback đếm đúng một lần");
        }
    }

    /// <summary>Outer timeout ném <see cref="DecisionTimeoutException"/> và ghi thời lượng <c>timeout</c>; không có fallback nào được đếm.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_OuterTimeout_RecordsTimeoutOutcome()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        using var pipeline = new StrategyPipeline(
            MsOptions.Create(Options(innerTimeoutMs: 60_000, outerTimeoutMs: 50)), TimeProvider.System, metrics, NullLogger<StrategyPipeline>.Instance);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token), strategyName: StrategyName);

        var act = () => RunAsync(pipeline, strategy);

        await act.Should().ThrowAsync<DecisionTimeoutException>();
        recorder.Count(TadssMetricNames.PipelineDuration, ("strategy", StrategyName), ("outcome", "timeout")).Should().Be(1);
        recorder.Sum(TadssMetricNames.Fallbacks).Should().Be(0);
    }

    /// <summary>Caller hủy giữa chừng thì không ghi số đo pipeline và không đếm fallback.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    [Trait("CatalogId", "UT-07")]
    public async Task ExecuteAsync_CallerCancelled_RecordsNothing()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        using var pipeline = new StrategyPipeline(MsOptions.Create(Options()), TimeProvider.System, metrics, NullLogger<StrategyPipeline>.Instance);
        using var callerCts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var strategy = new DelegateStrategy(
            (_, _, token) =>
            {
                started.TrySetResult();
                return WaitForever(token);
            },
            strategyName: StrategyName);

        var execution = RunAsync(pipeline, strategy, callerCts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await callerCts.CancelAsync();

        await execution.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        recorder.Count(TadssMetricNames.PipelineDuration).Should().Be(0);
        recorder.Sum(TadssMetricNames.Fallbacks).Should().Be(0);
    }

    /// <summary>
    /// Gauge breaker theo từng strategy: chỉ strategy đã có pipeline mới có số đo (strategy B chưa chạy thì không có); đóng (0) → mở (2) sau đủ lỗi →
    /// đang thăm dò (1, quan sát từ bên trong lượt thăm dò) → đóng lại (0) khi thăm dò thành công.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    [Trait("CatalogId", "UT-07")]
    public async Task CircuitStateGauge_FollowsBreakerTransitions()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var time = new FakeTimeProvider();
        using var pipeline = new StrategyPipeline(
            MsOptions.Create(Options(minimumThroughput: 2, breakSeconds: 5)), time, metrics, NullLogger<StrategyPipeline>.Instance);
        var failing = true;
        IReadOnlyList<MetricRecorder.GaugeSample>? observedDuringProbe = null;
        var strategy = new DelegateStrategy(
            (_, _, _) =>
            {
                if (failing)
                {
                    return Throwing();
                }

                observedDuringProbe = recorder.ObserveGauge(TadssMetricNames.CircuitState);
                return Task.FromResult(Approved());
            },
            strategyName: "StrategyA");

        recorder.ObserveGauge(TadssMetricNames.CircuitState).Should().BeEmpty("chưa strategy nào có pipeline");

        await RunAsync(pipeline, strategy);
        StateOf(recorder, "StrategyA").Should().Be(0, "một lỗi chưa đủ ngưỡng thông lượng");
        await RunAsync(pipeline, strategy);
        StateOf(recorder, "StrategyA").Should().Be(2, "hai lỗi liên tiếp làm breaker mở");
        recorder.ObserveGauge(TadssMetricNames.CircuitState).Should().ContainSingle("strategy B chưa chạy nên chưa có series");

        failing = false;
        time.Advance(TimeSpan.FromSeconds(6));
        await RunAsync(pipeline, strategy);

        observedDuringProbe.Should().NotBeNull();
        observedDuringProbe!.Single(sample => sample.Tags["strategy"] == "StrategyA").Value.Should().Be(1, "đang thăm dò thì nửa mở");
        StateOf(recorder, "StrategyA").Should().Be(0, "thăm dò thành công thì breaker đóng lại");
    }

    private static long StateOf(MetricRecorder recorder, string strategy) =>
        recorder.ObserveGauge(TadssMetricNames.CircuitState).Single(sample => sample.Tags["strategy"] == strategy).Value;
}
