using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Infrastructure.Resilience;

namespace TADSS.Runtime.Api.V2.Infrastructure.Resilience;

/// <summary>
/// Pipeline chịu lỗi của strategy v2 (Polly v8, giữ thứ tự của v1 theo SAS 4.2):
/// Outer timeout → Fallback → Retry → Circuit breaker → Inner timeout, tham số lấy từ <see cref="ResilienceOptions"/>.
/// <para>
/// Pipeline là singleton dùng chung cho mọi strategy nên chỉ có <b>một</b> circuit breaker cho tất cả strategy
/// (breaker theo từng strategy sẽ làm ở gói 5.4 — V-2). Đây là instance riêng của v2, không chia sẻ trạng thái breaker
/// với pipeline v1. <see cref="TimeProvider"/> được gán cho cả pipeline Polly nên timeout, retry delay và breaker cùng
/// chạy theo đồng hồ được tiêm (test dùng <c>FakeTimeProvider</c>).
/// </para>
/// </summary>
public sealed class StrategyPipeline : IStrategyPipeline
{
    private readonly ResilienceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StrategyPipeline> _logger;
    private readonly ResiliencePipeline<StrategyExecutionOutcome> _pipeline;

    /// <summary>Khởi tạo pipeline từ cấu hình <c>Tadss:Resilience</c>.</summary>
    public StrategyPipeline(IOptions<ResilienceOptions> options, TimeProvider timeProvider, ILogger<StrategyPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _pipeline = BuildPipeline();
    }

    /// <inheritdoc />
    public async Task<StrategyExecutionOutcome> ExecuteAsync(
        IReservationDecisionStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = _timeProvider.GetTimestamp();

        var outcome = await _pipeline.ExecuteAsync(
            static async (state, token) =>
            {
                var decision = await state.Strategy.EvaluateAsync(state.Reservation, state.Context, token).ConfigureAwait(false);

                // Strategy trả null là lỗi hợp đồng: đi chung đường lỗi để được retry rồi fallback
                if (decision is null)
                {
                    throw new InvalidOperationException("Strategy trả về null thay vì StrategyDecision.");
                }

                return new StrategyExecutionOutcome(decision, ExecutionType.AUTOMATED, FallbackUsed: false, FailureReason: null, ExecutionTimeMs: 0);
            },
            (Strategy: strategy, Reservation: reservation, Context: context),
            cancellationToken).ConfigureAwait(false);

        var elapsed = _timeProvider.GetElapsedTime(startedAt);
        var elapsedMs = (int)Math.Max(0d, Math.Floor(elapsed.TotalMilliseconds));

        return outcome with { ExecutionTimeMs = elapsedMs };
    }

    private ResiliencePipeline<StrategyExecutionOutcome> BuildPipeline()
    {
        var builder = new ResiliencePipelineBuilder<StrategyExecutionOutcome> { TimeProvider = _timeProvider };

        // 1. Outer timeout: hạn cứng của cả pipeline; nằm ngoài Fallback nên TimeoutRejectedException lan ra (504)
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(_options.OuterTimeoutMs),
            OnTimeout = _ =>
            {
                _logger.LogError("Pipeline strategy v2 vượt Outer timeout ({TimeoutMs} ms).", _options.OuterTimeoutMs);
                return default;
            }
        });

        // 2. Fallback: timeout / lỗi / circuit mở của strategy -> quyết định NEED_APPROVAL; hủy từ caller không bị bắt
        builder.AddFallback(new FallbackStrategyOptions<StrategyExecutionOutcome>
        {
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: true)),
            FallbackAction = args =>
            {
                var outcome = CreateFallbackOutcome(args.Outcome.Exception);
                _logger.LogWarning(
                    args.Outcome.Exception,
                    "Fallback strategy v2: ReasonCode {ReasonCode}, FailureReason {FailureReason}.",
                    outcome.Decision.ReasonCode,
                    outcome.FailureReason);
                return new ValueTask<Outcome<StrategyExecutionOutcome>>(Outcome.FromResult(outcome));
            }
        });

        // 3. Retry: lỗi nhất thời; không retry khi circuit mở hoặc bị hủy
        if (_options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<StrategyExecutionOutcome>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                Delay = TimeSpan.FromMilliseconds(_options.RetryBaseDelayMs),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: false)),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry strategy v2 lần {AttemptNumber}/{MaxAttempts} do {ExceptionType}.",
                        args.AttemptNumber + 1,
                        _options.MaxRetryAttempts,
                        args.Outcome.Exception?.GetType().Name);
                    return default;
                }
            });
        }

        // 4. Circuit breaker: cách ly khi tỷ lệ lỗi vượt ngưỡng trong khung lấy mẫu
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<StrategyExecutionOutcome>
        {
            FailureRatio = _options.CircuitBreakerFailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(_options.CircuitBreakerSamplingSeconds),
            MinimumThroughput = _options.CircuitBreakerMinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds),
            ShouldHandle = args => ValueTask.FromResult(IsFailure(args.Outcome, args.Context, includeBrokenCircuit: false)),
            OnOpened = args =>
            {
                _logger.LogError("Circuit breaker strategy v2 MỞ trong {BreakDuration}s do tỷ lệ lỗi vượt ngưỡng.", args.BreakDuration.TotalSeconds);
                return default;
            },
            OnClosed = _ =>
            {
                _logger.LogInformation("Circuit breaker strategy v2 ĐÓNG: chạy strategy bình thường trở lại.");
                return default;
            },
            OnHalfOpened = _ =>
            {
                _logger.LogInformation("Circuit breaker strategy v2 NỬA MỞ: đang thăm dò sức khỏe strategy.");
                return default;
            }
        });

        // 5. Inner timeout: giới hạn cho từng lượt thử
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromMilliseconds(_options.InnerTimeoutMs),
            OnTimeout = _ =>
            {
                _logger.LogWarning("Một lượt thử strategy v2 vượt Inner timeout ({InnerTimeoutMs} ms).", _options.InnerTimeoutMs);
                return default;
            }
        });

        return builder.Build();
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

    private StrategyExecutionOutcome CreateFallbackOutcome(Exception? exception)
    {
        var (cause, failureReason) = exception switch
        {
            TimeoutRejectedException => (FallbackCause.Timeout, FailureReasonPrefixes.FormatTimeout(_options.InnerTimeoutMs)),
            BrokenCircuitException => (FallbackCause.CircuitOpen, FailureReasonPrefixes.FormatCircuitBreakerOpen()),
            // Chỉ ghi tên kiểu: message của exception có thể chứa chi tiết nội bộ, không được đi vào audit/response
            _ => (FallbackCause.Exception, FailureReasonPrefixes.FormatException(exception?.GetType().Name))
        };

        return new StrategyExecutionOutcome(
            FallbackDecisionPolicy.Create(cause),
            ExecutionType.FALLBACK,
            FallbackUsed: true,
            FailureReason: failureReason,
            ExecutionTimeMs: 0);
    }
}
