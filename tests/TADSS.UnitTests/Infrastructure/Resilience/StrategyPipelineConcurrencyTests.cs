using FluentAssertions;
using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// UT-07 / SCALE-1 (hồi quy): nhiều request đầu tiên của cùng một strategy mới đến <b>cùng lúc</b> không được làm hỏng việc dựng pipeline. Registry của Polly
/// có thể gọi callback cấu hình nhiều lần khi tranh chấp, còn <c>CircuitBreakerStateProvider</c> (đọc trạng thái breaker cho gauge) chỉ gắn được vào một breaker;
/// pipeline và provider của mỗi strategy phải được dựng đúng một lần.
/// </summary>
public class StrategyPipelineConcurrencyTests
{
    private const int StrategyCount = 12;
    private const int CallsPerStrategy = 16;

    /// <summary>Gọi đồng thời lần đầu cho nhiều strategy: mọi lượt thành công (không 500), mỗi strategy dựng đúng một pipeline và có đúng một series gauge.</summary>
    [Fact]
    [Trait("CatalogId", "UT-07")]
    [Trait("CatalogId", "SCALE-1")]
    public async Task ExecuteAsync_ConcurrentFirstCalls_BuildEachPipelineOnceAndAllSucceed()
    {
        using var metrics = new TadssMetrics();
        using var recorder = new MetricRecorder(metrics.Meter);
        var logger = new CapturingLogger<StrategyPipeline>();
        var options = new ResilienceOptions { OuterTimeoutMs = 5_000, InnerTimeoutMs = 2_000, MaxRetryAttempts = 0 };
        using var pipeline = new StrategyPipeline(MsOptions.Create(options), TimeProvider.System, metrics, logger);
        var strategies = Enumerable.Range(0, StrategyCount)
            .Select(index => new DelegateStrategy(
                (_, _, _) => Task.FromResult(StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng thành viên."])),
                strategyName: $"ConcurrentStrategy{index}"))
            .ToList();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var executions = strategies
            .SelectMany(strategy => Enumerable.Range(0, CallsPerStrategy).Select(_ => Task.Run(async () =>
            {
                await startGate.Task;
                return await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);
            })))
            .ToList();
        startGate.SetResult();
        var outcomes = await Task.WhenAll(executions);

        outcomes.Should().OnlyContain(outcome => !outcome.FallbackUsed, "không lượt nào được hỏng vì tranh chấp dựng pipeline");
        foreach (var strategy in strategies)
        {
            logger.Entries
                .Count(entry => entry.Level == LogLevel.Debug && entry.Message.EndsWith($"{strategy.StrategyName}.", StringComparison.Ordinal))
                .Should().Be(1, $"{strategy.StrategyName} chỉ được dựng pipeline một lần");
        }

        recorder.ObserveGauge(TadssMetricNames.CircuitState).Select(sample => sample.Tags["strategy"])
            .Should().BeEquivalentTo(strategies.Select(strategy => strategy.StrategyName), "mỗi strategy có đúng một series trạng thái breaker");
    }
}
