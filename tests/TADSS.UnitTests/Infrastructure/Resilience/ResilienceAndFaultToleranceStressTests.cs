using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using Xunit;

namespace TADSS.UnitTests.Infrastructure.Resilience;

/// <summary>
/// Bộ kiểm thử chịu lỗi và sức bền nâng cao (Resilience & Fault Tolerance Stress Tests).
/// Kiểm thử các kịch bản thực tế phức tạp: Lỗi nhất thời tự phục hồi sau Retry, cạn kiệt số lần thử (Retry Exhaustion),
/// cắt mạch bảo vệ (Circuit Breaker tripping), hủy tác vụ khẩn cấp (Graceful Cancellation), và chạy tải 50 luồng đồng thời.
/// </summary>
public class ResilienceAndFaultToleranceStressTests
{
    private static Reservation CreateTestReservation()
    {
        return Reservation.Create(
            reservationId: Guid.NewGuid(),
            customerId: Guid.NewGuid(),
            customerTier: "DIAMOND",
            courtId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            courtTypeId: null,
            startTime: DateTime.UtcNow.AddDays(1),
            endTime: DateTime.UtcNow.AddDays(1).AddHours(2),
            totalPrice: 400000m,
            depositAmount: 200000m,
            paymentStatus: PaymentStatus.PARTIALLY_PAID,
            bookingType: "SINGLE",
            priority: BookingPriority.HIGH,
            requestedAt: DateTime.UtcNow);
    }

    private static DecisionResiliencePipeline CreatePipeline(ResilienceOptions? options = null)
    {
        var fallbackStrategy = new FallbackDecisionStrategy();
        var logger = NullLogger<DecisionResiliencePipeline>.Instance;
        return new DecisionResiliencePipeline(options ?? new ResilienceOptions(), fallbackStrategy, logger);
    }

    #region 1. Flaky Strategy Transient Recovery Tests

    private class FlakyStrategy : IReservationDecisionStrategy
    {
        private readonly int _failCountBeforeSuccess;
        public string StrategyName => "FlakyStrategy";
        public string StrategyVersion => "1.0.0";
        public int AttemptsCount { get; private set; }

        public FlakyStrategy(int failCountBeforeSuccess)
        {
            _failCountBeforeSuccess = failCountBeforeSuccess;
        }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            AttemptsCount++;
            if (AttemptsCount <= _failCountBeforeSuccess)
            {
                throw new TimeoutException($"Database connection timed out at attempt #{AttemptsCount}");
            }

            return Task.FromResult(new DecisionResultDto
            {
                DecisionId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                Status = DecisionStatus.APPROVED.ToString(),
                StrategyName = StrategyName,
                StrategyVersion = StrategyVersion,
                ExecutionTimeMs = 5,
                FallbackUsed = false,
                PrimaryReason = ErrorCodes.ApprovedStandard,
                Reasons = ["Tự phục hồi thành công sau lỗi nhất thời"],
                Recommendations = [],
                EvaluatedAt = DateTime.UtcNow
            });
        }
    }

    [Fact]
    public async Task ResiliencePipeline_TransientFailureTwice_RecoversOnThirdAttempt()
    {
        // Arrange: MaxRetry = 3, BaseDelay = 20ms
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 3,
            RetryBaseDelayMs = 20,
            InnerTimeoutMs = 300,
            OuterTimeoutMs = 1500
        };
        var pipeline = CreatePipeline(options);
        var flakyStrategy = new FlakyStrategy(failCountBeforeSuccess: 2);
        var reservation = CreateTestReservation();

        // Act
        var outcome = await pipeline.ExecuteStrategyAsync(flakyStrategy, reservation);

        // Assert
        flakyStrategy.AttemptsCount.Should().Be(3);
        outcome.FallbackUsed.Should().BeFalse();
        outcome.ExecutionType.Should().Be(ExecutionType.AUTOMATED);
        outcome.Result.Status.Should().Be(DecisionStatus.APPROVED.ToString());
    }

    [Fact]
    public async Task ResiliencePipeline_RetryExhaustion_ActivatesFallbackGracefully()
    {
        // Arrange: MaxRetry = 2, nhưng Strategy lỗi 5 lần liên tiếp
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 2,
            RetryBaseDelayMs = 10,
            InnerTimeoutMs = 200,
            OuterTimeoutMs = 1000
        };
        var pipeline = CreatePipeline(options);
        var persistentFailStrategy = new FlakyStrategy(failCountBeforeSuccess: 5);
        var reservation = CreateTestReservation();

        // Act
        var outcome = await pipeline.ExecuteStrategyAsync(persistentFailStrategy, reservation);

        // Assert
        // Attempt ban đầu (1) + 2 lần Retry = 3 lần thực thi
        persistentFailStrategy.AttemptsCount.Should().Be(3);
        outcome.FallbackUsed.Should().BeTrue();
        outcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        outcome.Result.Status.Should().Be("NEED_APPROVAL");
        outcome.Result.PrimaryReason.Should().Be(ErrorCodes.FallbackExecutionError);
    }

    #endregion

    #region 2. Circuit Breaker Rapid Tripping & Isolation

    private class CrashStrategy : IReservationDecisionStrategy
    {
        public string StrategyName => "CrashStrategy";
        public string StrategyVersion => "1.0.0";
        public int Invocations { get; private set; }

        public Task<DecisionResultDto> EvaluateAsync(Reservation reservation, CancellationToken cancellationToken = default)
        {
            Invocations++;
            throw new InvalidOperationException("Fatal external service failure.");
        }
    }

    [Fact]
    public async Task CircuitBreaker_TripsOpenAfterThreshold_IsolatesStrategyFromFurtherCalls()
    {
        // Arrange: Cần 3 thất bại để mở Circuit Breaker (BreakDuration = 30s)
        var options = new ResilienceOptions
        {
            CircuitBreakerMinimumThroughput = 3,
            CircuitBreakerFailureRatio = 0.5,
            CircuitBreakerSamplingSeconds = 30,
            CircuitBreakerBreakDurationSeconds = 30,
            MaxRetryAttempts = 0
        };
        var pipeline = CreatePipeline(options);
        var crashStrategy = new CrashStrategy();
        var reservation = CreateTestReservation();

        // Act 1: 3 lần gọi đầu tiên gây lỗi khiến Circuit Breaker kích hoạt OPEN
        for (int i = 0; i < 3; i++)
        {
            await pipeline.ExecuteStrategyAsync(crashStrategy, reservation);
        }

        var invocationsBefore = crashStrategy.Invocations;
        invocationsBefore.Should().Be(3);

        // Act 2: Lần gọi thứ 4 khi Circuit Breaker đã OPEN
        var isolatedOutcome = await pipeline.ExecuteStrategyAsync(crashStrategy, reservation);

        // Assert: Không hề gọi vào crashStrategy nữa (Invocations không đổi), chuyển thẳng sang Fallback
        crashStrategy.Invocations.Should().Be(3);
        isolatedOutcome.ExecutionType.Should().Be(ExecutionType.FALLBACK);
        isolatedOutcome.FallbackUsed.Should().BeTrue();
        isolatedOutcome.FailureReason.Should().Contain("Circuit breaker is open");
        isolatedOutcome.Result.PrimaryReason.Should().Be(ErrorCodes.FallbackSafetyMode);
    }

    #endregion

    #region 3. Pre-Cancelled CancellationToken Fast Fail

    [Fact]
    public async Task ResiliencePipeline_PreCancelledToken_AbortsInstantlyWithoutExecution()
    {
        // Arrange
        var pipeline = CreatePipeline();
        var strategy = new FlakyStrategy(failCountBeforeSuccess: 0);
        var reservation = CreateTestReservation();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Hủy ngay trước khi gọi

        // Act
        var act = () => pipeline.ExecuteStrategyAsync(strategy, reservation, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        strategy.AttemptsCount.Should().Be(0);
    }

    #endregion

    #region 4. High-Concurrency Stress Test (50 Tasks on Resilience Pipeline)

    [Fact]
    public async Task ResiliencePipeline_50ConcurrentSuccessfulRequests_ExecutesDeterministically()
    {
        // Arrange: 50 requests đồng thời chạy qua cùng một Resilience Pipeline instance
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 1,
            RetryBaseDelayMs = 10,
            InnerTimeoutMs = 500,
            OuterTimeoutMs = 2000
        };
        var pipeline = CreatePipeline(options);
        var reservation = CreateTestReservation();

        // Act: 50 requests đồng thời chạy qua Resilience Pipeline
        var tasks = Enumerable.Range(0, 50).Select(_ =>
        {
            var strategy = new FlakyStrategy(failCountBeforeSuccess: 0);
            return pipeline.ExecuteStrategyAsync(strategy, reservation);
        });

        var results = await Task.WhenAll(tasks);

        // Assert: Tất cả 50 requests đều thành công, không deadlock, không lỗi luồng
        results.Length.Should().Be(50);
        results.Should().OnlyContain(r => r.ExecutionType == ExecutionType.AUTOMATED && !r.FallbackUsed);
        results.Should().OnlyContain(r => r.Result.Status == DecisionStatus.APPROVED.ToString());
    }

    [Fact]
    public async Task ResiliencePipeline_50ConcurrentMixedRequests_SafelyHandlesFailuresWithoutCrashing()
    {
        // Arrange
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 1,
            RetryBaseDelayMs = 10,
            InnerTimeoutMs = 500,
            OuterTimeoutMs = 2000
        };
        var pipeline = CreatePipeline(options);
        var reservation = CreateTestReservation();

        // Act: 50 requests đồng thời gồm cả thành công và thất bại
        var tasks = Enumerable.Range(0, 50).Select(i =>
        {
            IReservationDecisionStrategy strategy = (i % 2 == 0)
                ? new FlakyStrategy(failCountBeforeSuccess: 0)
                : new FlakyStrategy(failCountBeforeSuccess: 10);

            return pipeline.ExecuteStrategyAsync(strategy, reservation);
        });

        var results = await Task.WhenAll(tasks);

        // Assert: Toàn bộ 50 request đều có outcome an toàn (hoặc AUTOMATED hoặc FALLBACK), không exception
        results.Length.Should().Be(50);
        results.Should().OnlyContain(r => r.Result != null && r.Result.DecisionId != Guid.Empty);
    }

    #endregion
}
