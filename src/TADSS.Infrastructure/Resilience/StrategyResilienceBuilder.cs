using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Resilience;

/// <summary>
/// Dựng các lớp Polly của <b>một</b> strategy (A-05): Outer timeout → Fallback → Retry → Circuit breaker → Inner timeout, tham số lấy từ
/// <see cref="ResilienceOptions"/> dùng chung cho mọi strategy. Mỗi strategy có pipeline (và circuit breaker) riêng nên mọi log đều mang
/// <c>{StrategyName}</c>. <see cref="TimeProvider"/> do bên gọi gán cho <c>builder</c> trước khi vào đây.
/// </summary>
internal static class StrategyResilienceBuilder
{
    /// <summary>Thêm các lớp chịu lỗi vào <paramref name="builder"/>.</summary>
    /// <param name="builder">Builder của pipeline strategy; bên gọi đã gán <c>TimeProvider</c>.</param>
    /// <param name="options">Tham số chịu lỗi dùng chung.</param>
    /// <param name="strategyName">Tên strategy sở hữu pipeline, đưa vào mọi log.</param>
    /// <param name="logger">Logger của pipeline.</param>
    /// <param name="createFallbackOutcome">Ánh xạ exception cuối cùng thành quyết định fallback (cọc 100%).</param>
    public static void Configure(
        ResiliencePipelineBuilder<StrategyExecutionOutcome> builder,
        ResilienceOptions options,
        string strategyName,
        ILogger logger,
        Func<Exception?, StrategyExecutionOutcome> createFallbackOutcome)
    {
        // 1. Outer timeout: hạn cứng của cả pipeline; nằm ngoài Fallback nên lỗi lan ra và được dịch thành DecisionTimeoutException (504)
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(options.OuterTimeoutMs),
            OnTimeout = _ =>
            {
                logger.LogError("Pipeline strategy {StrategyName} vượt Outer timeout ({TimeoutMs} ms).", strategyName, options.OuterTimeoutMs);
                return default;
            }
        });

        // 2. Fallback: timeout / lỗi / circuit mở của strategy -> quyết định APPROVED cọc 100%; hủy từ caller không bị bắt
        builder.AddFallback(new FallbackStrategyOptions<StrategyExecutionOutcome>
        {
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: true)),
            FallbackAction = args =>
            {
                var outcome = createFallbackOutcome(args.Outcome.Exception);
                logger.LogWarning(
                    args.Outcome.Exception,
                    "Fallback strategy {StrategyName}: ReasonCode {ReasonCode}, FailureReason {FailureReason}.",
                    strategyName,
                    outcome.Decision.ReasonCode,
                    outcome.FailureReason);
                return new ValueTask<Outcome<StrategyExecutionOutcome>>(Outcome.FromResult(outcome));
            }
        });

        // 3. Retry: lỗi nhất thời; không retry khi circuit mở hoặc bị hủy
        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<StrategyExecutionOutcome>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(options.RetryBaseDelayMs),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: false)),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Retry strategy {StrategyName} lần {AttemptNumber}/{MaxAttempts} do {ExceptionType}.",
                        strategyName,
                        args.AttemptNumber + 1,
                        options.MaxRetryAttempts,
                        args.Outcome.Exception?.GetType().Name);
                    return default;
                }
            });
        }

        // 4. Circuit breaker: cách ly khi tỷ lệ lỗi vượt ngưỡng trong khung lấy mẫu (riêng cho strategy này)
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<StrategyExecutionOutcome>
        {
            FailureRatio = options.CircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakerSamplingSeconds),
            MinimumThroughput = options.CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: false)),
            OnOpened = args =>
            {
                logger.LogError(
                    "Circuit breaker strategy {StrategyName} MỞ trong {BreakDuration}s do tỷ lệ lỗi vượt ngưỡng.",
                    strategyName,
                    args.BreakDuration.TotalSeconds);
                return default;
            },
            OnClosed = _ =>
            {
                logger.LogInformation("Circuit breaker strategy {StrategyName} ĐÓNG: chạy strategy bình thường trở lại.", strategyName);
                return default;
            },
            OnHalfOpened = _ =>
            {
                logger.LogInformation("Circuit breaker strategy {StrategyName} NỬA MỞ: đang thăm dò sức khỏe strategy.", strategyName);
                return default;
            }
        });

        // 5. Inner timeout: giới hạn cho từng lượt thử
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(options.InnerTimeoutMs),
            OnTimeout = _ =>
            {
                logger.LogWarning(
                    "Một lượt thử strategy {StrategyName} vượt Inner timeout ({InnerTimeoutMs} ms).",
                    strategyName,
                    options.InnerTimeoutMs);
                return default;
            }
        });
    }

    /// <summary>
    /// Phân biệt lỗi của strategy với hủy từ phía hệ thống. <see cref="OperationCanceledException"/> chỉ là lỗi của strategy
    /// khi token của lớp đang xét <b>chưa</b> bị hủy (vd. HttpClient timeout trong strategy ném <c>TaskCanceledException</c> — F3);
    /// token này là token của lớp Polly bao ngoài nên đã gộp cả hủy của caller và Outer timeout, hai trường hợp đó vẫn lan ra.
    /// Circuit mở (<see cref="BrokenCircuitException"/>) chỉ được fallback bắt, không tính vào retry/breaker.
    /// </summary>
    private static bool IsFailure(Outcome<StrategyExecutionOutcome> outcome, ResilienceContext context, bool includeBrokenCircuit)
    {
        var exception = outcome.Exception;
        if (exception is null)
        {
            return false;
        }

        if (exception is OperationCanceledException)
        {
            return !context.CancellationToken.IsCancellationRequested;
        }

        return includeBrokenCircuit || exception is not BrokenCircuitException;
    }
}
