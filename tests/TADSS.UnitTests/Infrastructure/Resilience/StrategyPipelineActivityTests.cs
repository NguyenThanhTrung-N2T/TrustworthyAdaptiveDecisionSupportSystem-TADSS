using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
/// OB-5 (phần trace của pipeline): mỗi lần chạy strategy có một span <c>tadss.strategy.execute</c> mang <c>tadss.strategy</c>, <c>tadss.outcome</c> và
/// (khi fallback) <c>tadss.fallback_reason</c>; span lỗi chỉ ghi <b>kiểu</b> exception, không ghi message. Strategy mỗi test có tên duy nhất để lọc span
/// khỏi các test chạy song song (<see cref="ActivityRecorder"/>).
/// </summary>
public class StrategyPipelineActivityTests
{
    private static ResilienceOptions Options(int innerTimeoutMs = 2_000, int outerTimeoutMs = 5_000) => new()
    {
        OuterTimeoutMs = outerTimeoutMs,
        InnerTimeoutMs = innerTimeoutMs,
        MaxRetryAttempts = 0,
        RetryBaseDelayMs = 1,
    };

    private static async Task<StrategyDecision> WaitForever(CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        throw new InvalidOperationException("Không được tới đây.");
    }

    /// <summary>Thành công → <c>automated</c>, không có lý do fallback; strategy ném lỗi → <c>fallback</c> + <c>exception</c>; quá hạn từng lượt → <c>fallback</c> + <c>timeout</c>.</summary>
    [Theory]
    [Trait("CatalogId", "OB-5")]
    [InlineData("success", "automated", null)]
    [InlineData("throws", "fallback", "exception")]
    [InlineData("inner-timeout", "fallback", "timeout")]
    public async Task ExecuteAsync_Outcome_SetsSpanAttributes_Theory(string scenario, string expectedOutcome, string? expectedReason)
    {
        var strategyName = $"SpanStrategy-{scenario}";
        using var recorder = new ActivityRecorder();
        using var metrics = new TadssMetrics();
        using var pipeline = new StrategyPipeline(
            MsOptions.Create(Options(innerTimeoutMs: scenario == "inner-timeout" ? 30 : 2_000)),
            TimeProvider.System,
            metrics,
            NullLogger<StrategyPipeline>.Instance);
        var strategy = scenario switch
        {
            "success" => new DelegateStrategy((_, _, _) => Task.FromResult(StrategyDecision.Approve(0.5m, ReasonCodes.TierDeposit, ["Cọc theo hạng."])), strategyName: strategyName),
            "inner-timeout" => new DelegateStrategy((_, _, token) => WaitForever(token), strategyName: strategyName),
            _ => new DelegateStrategy((_, _, _) => throw new InvalidOperationException("SECRET-chi-tiet-noi-bo"), strategyName: strategyName),
        };

        await pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        var span = recorder.Where(TadssActivitySource.StrategyExecute, TadssActivitySource.TagStrategy, strategyName).Should().ContainSingle().Subject;
        span.GetTagItem(TadssActivitySource.TagOutcome).Should().Be(expectedOutcome);
        span.GetTagItem(TadssActivitySource.TagFallbackReason).Should().Be(expectedReason);
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.Tags.Select(tag => tag.Value).Should().NotContain(value => value != null && value.Contains("SECRET"));
    }

    /// <summary>Outer timeout → <c>DecisionTimeoutException</c>; span có <c>tadss.outcome = timeout</c>, trạng thái Error và mô tả là tên kiểu exception.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task ExecuteAsync_OuterTimeout_MarksSpanErrorWithExceptionTypeOnly()
    {
        const string strategyName = "SpanStrategy-outer-timeout";
        using var recorder = new ActivityRecorder();
        using var metrics = new TadssMetrics();
        using var pipeline = new StrategyPipeline(
            MsOptions.Create(Options(innerTimeoutMs: 2_000, outerTimeoutMs: 50)),
            TimeProvider.System,
            metrics,
            NullLogger<StrategyPipeline>.Instance);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token), strategyName: strategyName);

        var act = () => pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<DecisionTimeoutException>();
        var span = recorder.Where(TadssActivitySource.StrategyExecute, TadssActivitySource.TagStrategy, strategyName).Should().ContainSingle().Subject;
        span.GetTagItem(TadssActivitySource.TagOutcome).Should().Be(TadssMetricNames.PipelineOutcomeTimeout);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("TimeoutRejectedException");
    }

    /// <summary>Hủy của caller không đặt <c>tadss.outcome</c> (không có kết quả để báo).</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task ExecuteAsync_CallerCancellation_SetsNoOutcome()
    {
        const string strategyName = "SpanStrategy-caller-cancel";
        using var recorder = new ActivityRecorder();
        using var metrics = new TadssMetrics();
        using var pipeline = new StrategyPipeline(MsOptions.Create(Options()), TimeProvider.System, metrics, NullLogger<StrategyPipeline>.Instance);
        var strategy = new DelegateStrategy((_, _, token) => WaitForever(token), strategyName: strategyName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        var act = () => pipeline.ExecuteAsync(strategy, DomainTestData.CreateReservation(), DomainTestData.CreateContext(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var span = recorder.Where(TadssActivitySource.StrategyExecute, TadssActivitySource.TagStrategy, strategyName).Should().ContainSingle().Subject;
        span.GetTagItem(TadssActivitySource.TagOutcome).Should().BeNull();
    }
}
