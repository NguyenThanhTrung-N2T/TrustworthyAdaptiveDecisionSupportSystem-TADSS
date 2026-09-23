using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;

namespace TADSS.Runtime.Api.Infrastructure.Resilience;

/// <summary>
/// Triển khai chuỗi chịu lỗi đa tầng (Nested Resilience Pipeline) sử dụng Polly v8 (Polly.Core).
/// Thứ tự bao bọc: Outer Timeout (500ms - Hard Runtime Execution Boundary) -> Fallback -> Retry -> Circuit Breaker -> Inner Timeout (150ms).
/// </summary>
public class DecisionResiliencePipeline : IDecisionResiliencePipeline
{
    private readonly ResilienceOptions _options;
    private readonly FallbackDecisionStrategy _fallbackStrategy;
    private readonly ILogger<DecisionResiliencePipeline> _logger;
    private readonly ResiliencePipeline<DecisionExecutionOutcome> _pipeline;

    public static readonly ResiliencePropertyKey<Reservation> ReservationPropertyKey = new("DecisionResilience_Reservation");
    public static readonly ResiliencePropertyKey<IReservationDecisionStrategy> StrategyPropertyKey = new("DecisionResilience_Strategy");

    public DecisionResiliencePipeline(
        IOptions<ResilienceOptions> options,
        FallbackDecisionStrategy fallbackStrategy,
        ILogger<DecisionResiliencePipeline> logger)
        : this(options?.Value ?? new ResilienceOptions(), fallbackStrategy, logger)
    {
    }

    public DecisionResiliencePipeline(
        ResilienceOptions options,
        FallbackDecisionStrategy fallbackStrategy,
        ILogger<DecisionResiliencePipeline> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fallbackStrategy = fallbackStrategy ?? throw new ArgumentNullException(nameof(fallbackStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _pipeline = BuildPipeline();
    }

    private ResiliencePipeline<DecisionExecutionOutcome> BuildPipeline()
    {
        var builder = new ResiliencePipelineBuilder<DecisionExecutionOutcome>();

        // 1. OUTER TIMEOUT STRATEGY (Hard Runtime Deadline toàn cục, mặc định 500ms)
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(_options.OuterTimeoutMs),
            OnTimeout = args =>
            {
                _logger.LogError("Outer timeout exceeded ({TimeoutMs}ms). Aborting decision pipeline.", _options.OuterTimeoutMs);
                return default;
            }
        });

        // 2. FALLBACK STRATEGY (Chế độ an toàn dự phòng cho dependency failure & inner timeouts)
        builder.AddFallback(new FallbackStrategyOptions<DecisionExecutionOutcome>
        {
            ShouldHandle = new PredicateBuilder<DecisionExecutionOutcome>()
                .Handle<BrokenCircuitException>()
                .Handle<TimeoutRejectedException>()
                .Handle<Exception>(ex => ex is not OperationCanceledException),
            FallbackAction = async args =>
            {
                var reservation = args.Context.Properties.GetValue(ReservationPropertyKey, null!);
                var exception = args.Outcome.Exception;

                var fallbackResult = await _fallbackStrategy.EvaluateAsync(reservation, args.Context.CancellationToken);

                string failureReason;
                if (exception is TimeoutRejectedException)
                {
                    fallbackResult.PrimaryReason = ErrorCodes.FallbackServiceTimeout;
                    failureReason = $"STRATEGY_TIMEOUT: Strategy execution timed out (> {_options.InnerTimeoutMs}ms per attempt).";
                }
                else if (exception is BrokenCircuitException)
                {
                    fallbackResult.PrimaryReason = ErrorCodes.FallbackSafetyMode;
                    failureReason = "CIRCUIT_BREAKER_OPEN: Circuit breaker is open due to high failure rate. Strategy execution isolated.";
                }
                else
                {
                    fallbackResult.PrimaryReason = ErrorCodes.FallbackExecutionError;
                    failureReason = $"STRATEGY_EXCEPTION: {exception?.Message ?? "Strategy execution failed with an unexpected error."}";
                }

                _logger.LogWarning("Resilience Fallback activated for Reservation {ReservationId}. PrimaryReason: {PrimaryReason}, FailureReason: {FailureReason}",
                    reservation?.ReservationId, fallbackResult.PrimaryReason, failureReason);

                return Outcome.FromResult(new DecisionExecutionOutcome
                {
                    Result = fallbackResult,
                    ExecutionType = ExecutionType.FALLBACK,
                    FallbackUsed = true,
                    FailureReason = failureReason,
                    ExecutionTimeMs = 0
                });
            }
        });

        // 3. RETRY STRATEGY (Retry cho lỗi nhất thời, LOẠI TRỪ BrokenCircuitException & OperationCanceledException)
        if (_options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<DecisionExecutionOutcome>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = new PredicateBuilder<DecisionExecutionOutcome>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException && ex is not OperationCanceledException),
                OnRetry = args =>
                {
                    _logger.LogWarning("Decision Strategy retry attempt {AttemptNumber}/{MaxAttempts} due to: {ErrorMessage}",
                        args.AttemptNumber + 1, _options.MaxRetryAttempts, args.Outcome.Exception?.Message);
                    return default;
                }
            });
        }

        // 4. CIRCUIT BREAKER (Cách ly lỗi khi tỷ lệ hỏng >= 50% trong khung sampling)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<DecisionExecutionOutcome>
        {
            FailureRatio = _options.CircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(_options.CircuitBreakerSamplingSeconds),
            MinimumThroughput = _options.CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds),
            ShouldHandle = new PredicateBuilder<DecisionExecutionOutcome>()
                .Handle<TimeoutRejectedException>()
                .Handle<Exception>(ex => ex is not BrokenCircuitException && ex is not OperationCanceledException),
            OnOpened = args =>
            {
                _logger.LogError("Circuit Breaker OPENED for {BreakDuration}s. Strategy failure ratio exceeded threshold.",
                    args.BreakDuration.TotalSeconds);
                return default;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("Circuit Breaker CLOSED. Normal strategy execution resumed.");
                return default;
            },
            OnHalfOpened = args =>
            {
                _logger.LogInformation("Circuit Breaker HALF-OPEN. Probing strategy health.");
                return default;
            }
        });

        // 5. INNER TIMEOUT (Timeout cho từng lần thực thi đơn lẻ, mặc định 150ms)
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(_options.InnerTimeoutMs),
            OnTimeout = args =>
            {
                _logger.LogWarning("Inner attempt timed out (> {InnerTimeoutMs}ms).", _options.InnerTimeoutMs);
                return default;
            }
        });

        return builder.Build();
    }

    public async Task<DecisionExecutionOutcome> ExecuteStrategyAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(reservation);

        var stopwatch = Stopwatch.StartNew();

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ReservationPropertyKey, reservation);
        context.Properties.Set(StrategyPropertyKey, strategy);

        try
        {
            var outcome = await _pipeline.ExecuteAsync(async state =>
            {
                var targetStrategy = state.Properties.GetValue(StrategyPropertyKey, strategy);
                var targetReservation = state.Properties.GetValue(ReservationPropertyKey, reservation);

                var result = await targetStrategy.EvaluateAsync(targetReservation, state.CancellationToken);

                return new DecisionExecutionOutcome
                {
                    Result = result,
                    ExecutionType = ExecutionType.AUTOMATED,
                    FallbackUsed = false,
                    FailureReason = null,
                    ExecutionTimeMs = 0
                };
            }, context);

            stopwatch.Stop();
            var elapsedMs = (int)stopwatch.ElapsedMilliseconds;

            outcome.Result.ExecutionTimeMs = elapsedMs;
            return outcome with { ExecutionTimeMs = elapsedMs };
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
