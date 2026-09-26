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
        private readonly Stopwatch _timeline = new();
        private readonly List<long> _attemptStartOffsetsMs = [];

        public string StrategyName => "SlowStrategy";
        public string StrategyVersion => "1.0.0";
        public int InvocationCount { get; private set; }
        public IReadOnlyList<long> AttemptStartOffsetsMs => _attemptStartOffsetsMs;

        public SlowStrategy(int delayMs)
        {
            _delayMs = delayMs;
        }

        public async Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (InvocationCount == 1)
            {
                _timeline.Restart();
                _attemptStartOffsetsMs.Add(0);
            }
            else
            {
                _attemptStartOffsetsMs.Add(_timeline.ElapsedMilliseconds);
            }

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
        // Arrange:
        // - OuterTimeoutMs = 270ms (Hard deadline toàn cục của bài test)
        // - InnerTimeoutMs = 120ms, RetryBaseDelayMs = 50ms, MaxRetryAttempts = 3 (tối đa 4 attempts nếu không bị chặn)
        // Lịch trình lý thuyết:
        // - Attempt 1: bắt đầu tại ~0ms, bị ngắt bởi InnerTimeout tại ~120ms -> chờ RetryDelay 50ms (~170ms)
        // - Attempt 2: bắt đầu tại ~170ms (< 270ms), đang chạy thì bị OuterTimeout cắt tại ~270ms
        // - Attempt 3 (~340ms) và Attempt 4 (~510ms) TUYỆT ĐỐI không được phép khởi chạy.
        const int outerTimeoutMs = 270;
        const int allowedSchedulingToleranceMs = 90; // Measurement/scheduling tolerance của môi trường test (không phải tăng OuterTimeout)

        var options = new ResilienceOptions
        {
            OuterTimeoutMs = outerTimeoutMs,
            InnerTimeoutMs = 120,
            MaxRetryAttempts = 3,
            RetryBaseDelayMs = 50
        };
        var pipeline = CreatePipeline(options);
        var reservation = CreateTestReservation();

        var slowStrategy = new SlowStrategy(delayMs: 2000);
        DecisionExecutionOutcome? outcome = null;
        Exception? caughtException = null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            outcome = await pipeline.ExecuteStrategyAsync(slowStrategy, reservation);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        stopwatch.Stop();

        // 1. Tổng thời gian thực thi phải bám sát outerTimeoutMs (270ms) + dung sai lập lịch môi trường test
        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(120);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(
            outerTimeoutMs + allowedSchedulingToleranceMs,
            "total execution time must be strictly capped by OuterTimeoutMs (270ms) plus test environment scheduling tolerance");

        // 2. Chứng minh bằng timestamp từng lần gọi: Không có bất kỳ attempt/retry mới nào được phép bắt đầu tại hoặc sau Outer Deadline (270ms)
        slowStrategy.AttemptStartOffsetsMs.Should().NotBeEmpty();
        slowStrategy.AttemptStartOffsetsMs.Should().OnlyContain(
            attemptStartMs => attemptStartMs < outerTimeoutMs,
            "no retry attempt may start at or after the 270ms outer deadline");
        slowStrategy.InvocationCount.Should().BeLessThanOrEqualTo(
            2,
            "attempt 3 (~340ms) and attempt 4 (~510ms) exceed the 270ms outer deadline and must never be invoked");

        // 3 & 4. Outcome hợp lệ: Hoặc OuterTimeout thoát ra ngoài (TimeoutRejectedException),
        // hoặc chuỗi Retry bị hủy sau InnerTimeout và được Fallback chuyển về trạng thái an toàn NEED_APPROVAL.
        if (caughtException != null)
        {
            caughtException.Should().BeOfType<TimeoutRejectedException>();
        }
        else
        {
            outcome.Should().NotBeNull();
            outcome!.ExecutionType.Should().Be(ExecutionType.FALLBACK);
            outcome.FallbackUsed.Should().BeTrue();
            outcome.Result.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        }
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

    [Fact]
    public async Task Fallback_MissingReservationContext_DoesNotCrash()
    {
        var fallbackStrategy = new FallbackDecisionStrategy();

        var resultFromNullSafe = await fallbackStrategy.EvaluateFailSafeAsync(reservation: null);
        var resultFromInterface = await fallbackStrategy.EvaluateAsync(reservation: null!);

        resultFromNullSafe.Should().NotBeNull();
        resultFromNullSafe.ReservationId.Should().Be(Guid.Empty);
        resultFromNullSafe.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        resultFromNullSafe.FallbackUsed.Should().BeTrue();
        resultFromNullSafe.PrimaryReason.Should().Be(ErrorCodes.FallbackSafetyMode);
        resultFromNullSafe.ExecutionTimeMs.Should().Be(0);

        resultFromInterface.Should().NotBeNull();
        resultFromInterface.ReservationId.Should().Be(Guid.Empty);
        resultFromInterface.Status.Should().Be(DecisionStatus.NEED_APPROVAL.ToString());
        resultFromInterface.FallbackUsed.Should().BeTrue();
    }
}
