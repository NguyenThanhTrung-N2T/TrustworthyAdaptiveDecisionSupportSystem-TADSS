using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Application.Options;
using TADSS.Application.Strategies;

namespace TADSS.Application.Shadow;

/// <summary>
/// Thực thi một việc shadow (A-11): chạy strategy shadow qua pipeline của chính nó (không cache, cùng <c>nowUtc</c> của bản <c>PRIMARY</c>),
/// ghép bản ghi <c>SHADOW</c> bằng <see cref="DecisionResponseComposer"/> (không khóa idempotency) và ghi qua <see cref="IDecisionEvaluationStore"/>.
/// <para>
/// Mọi lỗi của shadow (outer timeout, lỗi lưu trữ, lỗi bất ngờ) chỉ ghi log Warning và đếm <c>tadss.shadow.executions{outcome=failed}</c>, không ném;
/// chỉ hủy của chính worker (tắt ứng dụng) được lan ra. Strategy shadow lỗi hoặc treo vẫn tạo bản <c>SHADOW</c> là <c>FALLBACK</c> như mọi strategy.
/// Scoped vì phụ thuộc kho audit scoped.
/// </para>
/// </summary>
public sealed class ShadowExecutor
{
    private readonly ShadowOptions _options;
    private readonly IStrategyRegistry _registry;
    private readonly IStrategyPipeline _pipeline;
    private readonly DecisionResponseComposer _composer;
    private readonly IDecisionEvaluationStore _store;
    private readonly BranchCalendar _calendar;
    private readonly TadssMetrics _metrics;
    private readonly ILogger<ShadowExecutor> _logger;

    /// <summary>Khởi tạo bộ thực thi shadow.</summary>
    /// <param name="options">Cấu hình shadow.</param>
    /// <param name="registry">Registry strategy (phân giải strategy shadow).</param>
    /// <param name="pipeline">Pipeline chịu lỗi (riêng cho từng strategy).</param>
    /// <param name="composer">Bộ ghép response và bản ghi audit.</param>
    /// <param name="store">Kho audit.</param>
    /// <param name="calendar">Lịch cao điểm đưa vào <see cref="DecisionContext"/>.</param>
    /// <param name="metrics">Metric của runtime.</param>
    /// <param name="logger">Logger.</param>
    public ShadowExecutor(
        IOptions<ShadowOptions> options,
        IStrategyRegistry registry,
        IStrategyPipeline pipeline,
        DecisionResponseComposer composer,
        IDecisionEvaluationStore store,
        BranchCalendar calendar,
        TadssMetrics metrics,
        ILogger<ShadowExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Chạy một việc shadow.</summary>
    /// <param name="item">Việc shadow.</param>
    /// <param name="cancellationToken">Token hủy của worker.</param>
    /// <exception cref="OperationCanceledException">Chỉ khi <paramref name="cancellationToken"/> bị hủy.</exception>
    public async Task ExecuteAsync(ShadowWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Việc shadow chạy trên worker nền nên là gốc của một trace mới, liên kết (không làm con) tới request đã sinh ra nó
        using var activity = StartActivity(item);
        try
        {
            var registered = _registry.ResolveByName(_options.Strategy);
            activity?.SetTag(TadssActivitySource.TagStrategy, registered.Strategy.StrategyName);
            var context = new DecisionContext(item.NowUtc, _calendar);
            var outcome = await _pipeline
                .ExecuteAsync(registered.Strategy, item.Reservation, context, cancellationToken)
                .ConfigureAwait(false);

            var composed = _composer.Compose(new DecisionComposition(
                DecisionId: Guid.NewGuid(),
                Request: item.Request,
                Reservation: item.Reservation,
                Strategy: registered,
                Outcome: outcome,
                CacheHit: false,
                NowUtc: item.NowUtc,
                RequestHash: item.RequestHash,
                IdempotencyKey: null,
                CorrelationId: item.CorrelationId,
                Role: ExecutionRole.SHADOW));

            await _store.InsertExecutionAsync(composed.Record, cancellationToken).ConfigureAwait(false);
            _metrics.RecordShadowExecution(TadssMetricNames.ShadowOutcomeRecorded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            TadssActivitySource.MarkError(activity, ex);
            _logger.LogWarning(
                "Shadow {StrategyName} thất bại ({ExceptionType}) cho đơn {ReservationId}, correlation {CorrelationId}; không có bản ghi SHADOW.",
                _options.Strategy,
                ex.GetType().Name,
                item.Reservation.ReservationId,
                item.CorrelationId);
            _metrics.RecordShadowExecution(TadssMetricNames.ShadowOutcomeFailed);
        }
    }

    private static Activity? StartActivity(ShadowWorkItem item)
    {
        var links = item.RequestActivityContext is { } requestContext ? [new ActivityLink(requestContext)] : Array.Empty<ActivityLink>();
        var activity = TadssActivitySource.Source.StartActivity(
            TadssActivitySource.ShadowExecute, ActivityKind.Internal, parentContext: default, links: links);
        activity?.SetTag(TadssActivitySource.TagCorrelationId, item.CorrelationId);
        return activity;
    }
}
