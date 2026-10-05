using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Resilience;

/// <summary>
/// Pipeline chịu lỗi của strategy (Polly, theo SAS 4.2): thực thi, dịch exception của Polly sang kiểu của Application và ánh xạ fallback.
/// <para>
/// Mỗi strategy có pipeline riêng, tạo lười lần đầu dùng trong một <see cref="ResiliencePipelineRegistry{TKey}"/> (khóa = <c>StrategyName</c>,
/// an toàn đa luồng) nên circuit breaker của strategy A mở không làm strategy B fast-fail. Các lớp Polly dựng ở
/// <see cref="StrategyResilienceBuilder"/>; lượt chạy trong cùng đi qua <see cref="IsolatedStrategyInvoker"/> để strategy không hợp tác
/// vẫn bị cắt đúng hạn. <see cref="TimeProvider"/> được gán cho mọi pipeline nên timeout, retry delay và breaker cùng chạy theo
/// đồng hồ được tiêm (test dùng <c>FakeTimeProvider</c>).
/// </para>
/// </summary>
public sealed class StrategyPipeline : IStrategyPipeline, IDisposable
{
    private readonly ResilienceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StrategyPipeline> _logger;
    private readonly IsolatedStrategyInvoker _invoker;
    private readonly ResiliencePipelineRegistry<string> _registry = new();
    private readonly Action<ResiliencePipelineBuilder<StrategyExecutionOutcome>, ConfigureBuilderContext<string>> _configurePipeline;

    /// <summary>Khởi tạo pipeline từ cấu hình <c>Tadss:Resilience</c>.</summary>
    public StrategyPipeline(
        IOptions<ResilienceOptions> options,
        TimeProvider timeProvider,
        TadssMetrics metrics,
        ILogger<StrategyPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);
        _options = options.Value;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _invoker = new IsolatedStrategyInvoker(metrics, logger);
        // Cache delegate để đường nóng ExecuteAsync không cấp phát delegate mới mỗi request
        _configurePipeline = ConfigurePipeline;
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

        var pipeline = _registry.GetOrAddPipeline<StrategyExecutionOutcome>(strategy.StrategyName, _configurePipeline);
        var startedAt = _timeProvider.GetTimestamp();

        StrategyExecutionOutcome outcome;
        try
        {
            outcome = await pipeline.ExecuteAsync(
                static async (state, token) =>
                {
                    var decision = await state.Invoker.InvokeAsync(state.Strategy, state.Reservation, state.Context, token).ConfigureAwait(false);

                    // Strategy trả null là lỗi hợp đồng: đi chung đường lỗi để được retry rồi fallback
                    if (decision is null)
                    {
                        throw new InvalidOperationException("Strategy trả về null thay vì StrategyDecision.");
                    }

                    return new StrategyExecutionOutcome(decision, ExecutionType.AUTOMATED, FallbackUsed: false, FailureReason: null, ExecutionTimeMs: 0);
                },
                (Invoker: _invoker, Strategy: strategy, Reservation: reservation, Context: context),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutRejectedException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Ưu tiên hủy của caller: Outer timeout và hủy của caller cùng xảy ra thì coi là client hủy (handler không ghi response)
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (TimeoutRejectedException ex)
        {
            // Dịch exception của Polly tại ranh giới adapter: lớp trên chỉ biết kiểu của Application
            throw new DecisionTimeoutException("Pipeline strategy vượt ngân sách thời gian cho phép.", ex);
        }

        var elapsed = _timeProvider.GetElapsedTime(startedAt);
        var elapsedMs = (int)Math.Max(0d, Math.Floor(elapsed.TotalMilliseconds));

        return outcome with { ExecutionTimeMs = elapsedMs };
    }

    /// <summary>Giải phóng registry (dispose các pipeline và timer của Polly) cùng singleton.</summary>
    public void Dispose() => _registry.Dispose();

    private void ConfigurePipeline(ResiliencePipelineBuilder<StrategyExecutionOutcome> builder, ConfigureBuilderContext<string> context)
    {
        builder.TimeProvider = _timeProvider;
        _logger.LogDebug("Dựng pipeline chịu lỗi cho strategy {StrategyName}.", context.PipelineKey);
        StrategyResilienceBuilder.Configure(builder, _options, context.PipelineKey, _logger, CreateFallbackOutcome);
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
