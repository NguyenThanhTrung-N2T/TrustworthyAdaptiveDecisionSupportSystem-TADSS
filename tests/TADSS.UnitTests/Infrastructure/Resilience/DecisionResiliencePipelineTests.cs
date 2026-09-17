using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Resilience;

public class DecisionResiliencePipelineTests
{
    private static Reservation CreateTestReservation()
    {
        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "VIP",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: DateTime.UtcNow.AddHours(2),
            endTime: DateTime.UtcNow.AddHours(4),
            totalPrice: 200000m,
            depositAmount: 200000m,
            paymentStatus: PaymentStatus.PAID,
            bookingType: "Single",
            priority: BookingPriority.NORMAL,
            requestedAt: DateTime.UtcNow
        );
    }

    private static DecisionResiliencePipeline CreatePipeline(ResilienceOptions? options = null)
    {
        var fallbackStrategy = new FallbackDecisionStrategy();
        var logger = NullLogger<DecisionResiliencePipeline>.Instance;
        return new DecisionResiliencePipeline(options ?? new ResilienceOptions(), fallbackStrategy, logger);
    }

    #region Fake Strategies for Resilience Testing

    private class AlwaysSuccessStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "AlwaysSuccessStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            var result = new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 5,
                FallbackUsed = false,
                PrimaryReason = "NORMAL_SUCCESS",
                Reasons = ["Thành công bình thường"],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
            return Task.FromResult(result);
        }
    }

    private class TransientFailureStrategy : IReservationDecisionStrategy
    {
        private readonly int _failsBeforeSuccess;
        public string StrategyName => "TransientFailureStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public TransientFailureStrategy(int failsBeforeSuccess)
        {
            _failsBeforeSuccess = failsBeforeSuccess;
        }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (InvocationCount <= _failsBeforeSuccess)
            {
                throw new InvalidOperationException($"Transient failure at attempt #{InvocationCount}");
            }

            var result = new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 10,
                FallbackUsed = false,
                PrimaryReason = "RETRY_RECOVERED",
                Reasons = ["Khôi phục thành công sau retry"],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
            return Task.FromResult(result);
        }
    }

    private class AlwaysThrowStrategy : IReservationDecisionStrategy
    {
        private readonly Exception _exceptionToThrow;
        public string StrategyName => "AlwaysThrowStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public AlwaysThrowStrategy(Exception? exceptionToThrow = null)
        {
            _exceptionToThrow = exceptionToThrow ?? new InvalidOperationException("Fatal strategy failure.");
        }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            throw _exceptionToThrow;
        }
    }

    private class SlowStrategy : IReservationDecisionStrategy
    {
        private readonly int _delayMs;
        public string StrategyName => "SlowStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }

        public SlowStrategy(int delayMs)
        {
            _delayMs = delayMs;
        }

        public async Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            await Task.Delay(_delayMs, cancellationToken);

            return new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = _delayMs,
                FallbackUsed = false,
                PrimaryReason = "DELAYED_SUCCESS",
                Reasons = [],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            };
        }
    }

    #endregion

    [Fact]
    public async Task Test01_NormalExecution_ReturnsAutomatedOutcomeWithoutFallback()
    {
        var pipeline = CreatePipeline();
        var strategy = new AlwaysSuccessStrategy();
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(strategy, reservation);

        strategy.InvocationCount.Should().Be(1);
        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.FallbackUsed.Should().BeFalse();
        outcome.FailureReason.Should().BeNull();
        outcome.Result.Status.Should().Be("APPROVED");
        outcome.Result.PrimaryReason.Should().Be("NORMAL_SUCCESS");
    }

    [Fact]
    public async Task Test02_TransientFailure_RetriesAndSucceedsOnSecondAttempt()
    {
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 10
        };
        var pipeline = CreatePipeline(options);
        var strategy = new TransientFailureStrategy(failsBeforeSuccess: 1);
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(strategy, reservation);

        strategy.InvocationCount.Should().Be(2);
        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.FallbackUsed.Should().BeFalse();
        outcome.FailureReason.Should().BeNull();
        outcome.Result.Status.Should().Be("APPROVED");
        outcome.Result.PrimaryReason.Should().Be("RETRY_RECOVERED");
    }

    [Fact]
    public async Task Test03_RepeatedStrategyException_ExhaustsRetriesAndTriggersFallback()
    {
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 10
        };
        var pipeline = CreatePipeline(options);
        var strategy = new AlwaysThrowStrategy(new InvalidOperationException("Fatal database down"));
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(strategy, reservation);

        strategy.InvocationCount.Should().Be(3); // 1 initial + 2 retries
        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.FailureReason.Should().Contain("Fatal database down");
        outcome.Result.Status.Should().Be("NEED_APPROVAL");
        outcome.Result.PrimaryReason.Should().Be(ErrorCodes.FallbackExecutionError);
    }

    [Fact]
    public async Task Test04_InnerTimeout_TriggersRetryThenFallback()
    {
        var options = new ResilienceOptions
        {
            InnerTimeoutMs = 30,
            OuterTimeoutMs = 2000,
            MaxRetryAttempts = 1,
            RetryBaseDelayMs = 10
        };
        var pipeline = CreatePipeline(options);
        var strategy = new SlowStrategy(delayMs: 150);
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(strategy, reservation);

        strategy.InvocationCount.Should().Be(2); // 1 initial + 1 retry
        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.Result.Status.Should().Be("NEED_APPROVAL");
        outcome.Result.PrimaryReason.Should().Be(ErrorCodes.FallbackServiceTimeout);
        outcome.FailureReason.Should().Contain("timed out");
    }

    [Fact]
    public async Task Test05_CircuitBreaker_OpensOnHighFailureRate()
    {
        var options = new ResilienceOptions
        {
            CircuitBreakerMinimumThroughput = 4,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerBreakDurationSeconds = 10,
            MaxRetryAttempts = 0
        };
        var pipeline = CreatePipeline(options);
        var throwStrategy = new AlwaysThrowStrategy(new InvalidOperationException("Dependency crash"));
        var reservation = CreateTestReservation();

        for (int i = 0; i < 4; i++)
        {
            await pipeline.ExecuteStrategyAsync(throwStrategy, reservation);
        }

        var outcome = await pipeline.ExecuteStrategyAsync(throwStrategy, reservation);

        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.FailureReason.Should().Contain("Circuit breaker is open");
        outcome.Result.Status.Should().Be("NEED_APPROVAL");
    }

    [Fact]
    public async Task Test06_Rule1_NoRetryOnBrokenCircuit()
    {
        var options = new ResilienceOptions
        {
            CircuitBreakerMinimumThroughput = 2,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerBreakDurationSeconds = 10,
            MaxRetryAttempts = 3
        };
        var pipeline = CreatePipeline(options);
        var throwStrategy = new AlwaysThrowStrategy();
        var reservation = CreateTestReservation();

        await pipeline.ExecuteStrategyAsync(throwStrategy, reservation);
        await pipeline.ExecuteStrategyAsync(throwStrategy, reservation);

        var freshStrategy = new AlwaysSuccessStrategy();
        var outcome = await pipeline.ExecuteStrategyAsync(freshStrategy, reservation);

        freshStrategy.InvocationCount.Should().Be(0);
        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.FailureReason.Should().Contain("Circuit breaker is open");
    }

    [Fact]
    public async Task Test07_OuterHardDeadline_ThrowsTimeoutRejectedException()
    {
        // Arrange: Outer Timeout 50ms, Strategy takes 300ms
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = 50,
            InnerTimeoutMs = 200,
            MaxRetryAttempts = 0
        };
        var pipeline = CreatePipeline(options);
        var slowStrategy = new SlowStrategy(delayMs: 300);
        var reservation = CreateTestReservation();

        // Act: Outer timeout ngắt toàn bộ pipeline và ném TimeoutRejectedException
        var act = () => pipeline.ExecuteStrategyAsync(slowStrategy, reservation);

        // Assert: Runtime execution contract vi phạm -> ném TimeoutRejectedException
        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task Test07B_MultipleRetriesCumulativeDelay_StrictlyCappedByOuterDeadline()
    {
        // Arrange: Outer Timeout 270ms, Inner Timeout 150ms, Retry Delay 50ms.
        // Attempt 0 mất 150ms (inner timeout) + delay 50ms = 200ms. Attempt 1 bắt đầu ở 200ms và bị ngắt ở 270ms bởi Outer Timeout (trước khi inner timeout 350ms hoặc các retry tiếp theo có thể chạy).
        var options = new ResilienceOptions
        {
            OuterTimeoutMs = 270,
            InnerTimeoutMs = 150,
            MaxRetryAttempts = 3,
            RetryBaseDelayMs = 50
        };
        var pipeline = CreatePipeline(options);
        var slowStrategy = new SlowStrategy(delayMs: 2000);
        var reservation = CreateTestReservation();

        var stopwatch = Stopwatch.StartNew();
        var act = () => pipeline.ExecuteStrategyAsync(slowStrategy, reservation);

        // Assert: Bị ngắt chặt chẽ bởi Outer Timeout và ném TimeoutRejectedException
        await act.Should().ThrowAsync<TimeoutRejectedException>();
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(200);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3500);
    }

    [Fact]
    public async Task Test08_Rule4_CancellationTokenRespected_ThrowsOperationCanceledException()
    {
        var pipeline = CreatePipeline();
        var slowStrategy = new SlowStrategy(delayMs: 500);
        var reservation = CreateTestReservation();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => pipeline.ExecuteStrategyAsync(slowStrategy, reservation, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Test09_Rule3_FallbackStructureAndSafetyInvariants()
    {
        var options = new ResilienceOptions { MaxRetryAttempts = 0 };
        var pipeline = CreatePipeline(options);
        var throwStrategy = new AlwaysThrowStrategy(new ArgumentException("Bad calculation data"));
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(throwStrategy, reservation);

        outcome.Result.Should().NotBeNull();
        outcome.Result.DecisionId.Should().NotBeEmpty();
        outcome.Result.ReservationId.Should().Be(reservation.ReservationId);
        outcome.Result.Status.Should().Be("NEED_APPROVAL");
        outcome.Result.FallbackUsed.Should().BeTrue();
        outcome.Result.PrimaryReason.Should().Be(ErrorCodes.FallbackExecutionError);
        outcome.Result.Recommendations.Should().ContainSingle(r =>
            r.Type == RecommendationType.REQUIRE_APPROVAL.ToString() && r.ConfidenceScore == 1.0);
    }

    [Fact]
    public async Task Test10_Telemetry_CapturesAccurateExecutionTime()
    {
        var pipeline = CreatePipeline();
        var delayStrategy = new SlowStrategy(delayMs: 40);
        var reservation = CreateTestReservation();

        var outcome = await pipeline.ExecuteStrategyAsync(delayStrategy, reservation);

        outcome.ExecutionTimeMs.Should().BeGreaterThanOrEqualTo(30);
        outcome.Result.ExecutionTimeMs.Should().Be(outcome.ExecutionTimeMs);
    }
}
