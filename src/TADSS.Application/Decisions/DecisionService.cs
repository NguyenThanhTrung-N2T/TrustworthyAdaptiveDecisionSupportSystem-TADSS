using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Application.Routing;
using TADSS.Application.Shadow;

namespace TADSS.Application.Decisions;

/// <summary>
/// Hiện thực <see cref="IDecisionService"/> theo luồng spec 4.2.3: kiểm tra khóa → dựng domain → hash → tra idempotency
/// → kiểm tra thời lượng item (P-01) → định tuyến strategy theo request (<see cref="IStrategyRouter"/>, A-01) → đánh giá (cache + pipeline, do <see cref="StrategyEvaluator"/>) → approval
/// window và cọc → ghi audit đồng bộ (fail-closed, INV-10) → gửi việc shadow (nếu bật) → response.
/// <para>
/// Một mốc <c>nowUtc</c> duy nhất lấy từ <see cref="TimeProvider"/>, cắt phần dưới micro giây, cho cả lần đánh giá (B-03, C-08). Lỗi trước bước ghi audit
/// (validation, P-01, xung đột idempotency) không ghi audit và không chạy strategy. Đăng ký scoped trong <c>AddApplication</c>;
/// <c>EfDecisionAuditStore</c> do <c>AddInfrastructure</c> đăng ký.
/// </para>
/// </summary>
public sealed class DecisionService : IDecisionService
{
    private readonly IDecisionEvaluationStore _evaluationStore;
    private readonly IStrategyRouter _router;
    private readonly StrategyEvaluator _evaluator;
    private readonly ItemDurationPolicy _itemDurationPolicy;
    private readonly DecisionResponseComposer _composer;
    private readonly IShadowDispatcher _shadowDispatcher;
    private readonly TadssMetrics _metrics;
    private readonly BranchCalendar _calendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionService> _logger;

    /// <summary>Khởi tạo service điều phối.</summary>
    /// <param name="evaluationStore">Kho audit (tra idempotency, ghi bản ghi <c>PRIMARY</c>).</param>
    /// <param name="router">Bộ định tuyến strategy theo request (<c>Tadss:Routing</c>).</param>
    /// <param name="evaluator">Bộ đánh giá strategy (cache + pipeline chịu lỗi).</param>
    /// <param name="itemDurationPolicy">Chính sách thời lượng item (P-01).</param>
    /// <param name="composer">Ghép response và bản ghi audit.</param>
    /// <param name="shadowDispatcher">Gửi việc shadow sau khi bản <c>PRIMARY</c> mới được lưu.</param>
    /// <param name="metrics">Metric của runtime (đếm bản <c>PRIMARY</c> mới).</param>
    /// <param name="calendar">Lịch cao điểm đưa vào <see cref="DecisionContext"/>.</param>
    /// <param name="timeProvider">Nguồn thời gian.</param>
    /// <param name="logger">Logger.</param>
    public DecisionService(
        IDecisionEvaluationStore evaluationStore,
        IStrategyRouter router,
        StrategyEvaluator evaluator,
        ItemDurationPolicy itemDurationPolicy,
        DecisionResponseComposer composer,
        IShadowDispatcher shadowDispatcher,
        TadssMetrics metrics,
        BranchCalendar calendar,
        TimeProvider timeProvider,
        ILogger<DecisionService> logger)
    {
        _evaluationStore = evaluationStore ?? throw new ArgumentNullException(nameof(evaluationStore));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _itemDurationPolicy = itemDurationPolicy ?? throw new ArgumentNullException(nameof(itemDurationPolicy));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _shadowDispatcher = shadowDispatcher ?? throw new ArgumentNullException(nameof(shadowDispatcher));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DecisionResultDto> EvaluateAsync(
        DecisionRequestDto request,
        string idempotencyKey,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        // Không có listener (telemetry tắt) thì activity là null và mọi lời gọi activity?. bên dưới là no-op
        using var activity = TadssActivitySource.Source.StartActivity(TadssActivitySource.DecisionEvaluate);
        activity?.SetTag(TadssActivitySource.TagCorrelationId, correlationId);
        try
        {
            return await EvaluateCoreAsync(request, idempotencyKey, correlationId, activity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hủy của caller không phải lỗi; mọi lỗi khác chỉ ghi kiểu exception lên span
            TadssActivitySource.MarkError(activity, ex);
            throw;
        }
    }

    private async Task<DecisionResultDto> EvaluateCoreAsync(
        DecisionRequestDto request,
        string idempotencyKey,
        string? correlationId,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureValidIdempotencyKey(idempotencyKey);

        // Dựng domain (thuần, không I/O) trước khi hash: request sai hình thức bị chặn bằng ContractValidationException
        // thay vì làm hỏng bước chuẩn hóa của RequestHasher. Kiểm tra thời lượng item (P-01, phụ thuộc cấu hình) đặt
        // sau bước idempotency để replay không bị ảnh hưởng khi cấu hình đổi.
        var reservation = request.ToDomain();
        var requestHash = RequestHasher.ComputeHash(request);

        var existing = await _evaluationStore.FindPrimaryByIdempotencyKeyAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            var replayed = ReplayStored(existing, requestHash, idempotencyKey);
            _logger.LogInformation(
                "Idempotency khớp khóa {IdempotencyKey}: trả lại quyết định {DecisionId} đã lưu.",
                idempotencyKey,
                replayed.DecisionId);
            return replayed;
        }

        _itemDurationPolicy.EnsureWithinLimits(reservation);

        var nowUtc = AuditTimestamps.TruncateToMicroseconds(_timeProvider.GetUtcNow());
        var route = _router.Route(reservation);
        var registered = route.Strategy;
        var context = new DecisionContext(nowUtc, _calendar);
        activity?.SetTag(TadssActivitySource.TagStrategy, registered.Strategy.StrategyName);
        activity?.SetTag(TadssActivitySource.TagRoutingRule, route.RuleName);

        var evaluation = await _evaluator
            .EvaluateAsync(registered, reservation, context, requestHash, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        activity?.SetTag(TadssActivitySource.TagCacheHit, evaluation.CacheHit);

        var composed = _composer.Compose(new DecisionComposition(
            DecisionId: Guid.NewGuid(),
            Request: request,
            Reservation: reservation,
            Strategy: registered,
            Outcome: evaluation.Outcome,
            CacheHit: evaluation.CacheHit,
            NowUtc: nowUtc,
            RequestHash: requestHash,
            IdempotencyKey: idempotencyKey,
            CorrelationId: correlationId));

        activity?.SetTag(TadssActivitySource.TagStatus, composed.Record.Status.ToString());
        activity?.SetTag(TadssActivitySource.TagExecutionType, composed.Record.ExecutionType.ToString());

        // Ghi audit đồng bộ, fail-closed (INV-10): lỗi lan ra và không có response nào được trả
        var inserted = await _evaluationStore.InsertExecutionAsync(composed.Record, cancellationToken).ConfigureAwait(false);
        if (inserted.IsExistingWinner)
        {
            var winner = ReplayStored(inserted.Stored, requestHash, idempotencyKey);
            _logger.LogInformation(
                "Tranh chấp idempotency đã được giải quyết cho khóa {IdempotencyKey}: trả quyết định {DecisionId} của request thắng.",
                idempotencyKey,
                winner.DecisionId);
            return winner;
        }

        _logger.LogDecisionSummary(composed.Record, evaluation.Outcome, registered.Strategy, route.RuleName, idempotencyKey, correlationId);

        // Chỉ đếm bản PRIMARY mới của chính request (không replay, không thua tranh chấp, không SHADOW) nên số đếm khớp số bản ghi (SC3)
        _metrics.RecordDecision(composed.Record.Status.ToString(), registered.Strategy.StrategyName, composed.Record.ExecutionType.ToString());

        // Shadow chỉ chạy khi chính request này vừa ghi bản PRIMARY mới (không phải replay hay thua tranh chấp); không chờ và không ném (INV-8)
        _shadowDispatcher.Dispatch(
            registered.Strategy.StrategyName,
            new ShadowWorkItem(request, reservation, requestHash, nowUtc, correlationId, activity?.Context));

        return composed.Response;
    }

    private static void EnsureValidIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (idempotencyKey.Length > ContractLimits.MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"Idempotency key không được dài quá {ContractLimits.MaxIdempotencyKeyLength} ký tự.",
                nameof(idempotencyKey));
        }
    }

    private static DecisionResultDto ReplayStored(DecisionExecutionRecord stored, string requestHash, string idempotencyKey)
    {
        if (!string.Equals(stored.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException(
                idempotencyKey,
                "Idempotency key đã được dùng cho một request có nội dung khác.");
        }

        // Giữ ngữ nghĩa cũ của replay: response đã lưu là JSON null → InvalidOperationException (payload hỏng vẫn là JsonException)
        return DecisionPayloadSerializer.DeserializeResponseOrNull(stored.ResponsePayload)
            ?? throw new InvalidOperationException("Response đã lưu của quyết định không đọc được.");
    }
}
