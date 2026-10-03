using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.DTOs.Mappings;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Persistence;
using TADSS.Application.Common;
using TADSS.Application.Exceptions;
using TADSS.Application.Strategies;

namespace TADSS.Application.Decisions;

/// <summary>
/// Hiện thực <see cref="IDecisionService"/> theo luồng spec 4.2.3: kiểm tra khóa → dựng domain → hash → tra idempotency
/// → kiểm tra thời lượng item (P-01) → chọn strategy → đánh giá (cache + pipeline, do <see cref="StrategyEvaluator"/>) → approval
/// window và cọc → ghi audit đồng bộ (fail-closed, INV-10) → response.
/// <para>
/// Một mốc <c>nowUtc</c> duy nhất lấy từ <see cref="TimeProvider"/>, cắt phần dưới micro giây, cho cả lần đánh giá (B-03, C-08). Lỗi trước bước ghi audit
/// (validation, P-01, xung đột idempotency) không ghi audit và không chạy strategy. Đăng ký scoped trong <c>AddApplication</c>;
/// <c>EfDecisionAuditStore</c> do <c>AddInfrastructure</c> đăng ký.
/// </para>
/// </summary>
public sealed class DecisionService : IDecisionService
{
    private readonly IDecisionEvaluationStore _evaluationStore;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly StrategyEvaluator _evaluator;
    private readonly ItemDurationPolicy _itemDurationPolicy;
    private readonly DecisionResponseComposer _composer;
    private readonly BranchCalendar _calendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionService> _logger;

    /// <summary>Khởi tạo service điều phối.</summary>
    /// <param name="evaluationStore">Kho audit (tra idempotency, ghi bản ghi <c>PRIMARY</c>).</param>
    /// <param name="strategyRegistry">Danh bạ strategy.</param>
    /// <param name="evaluator">Bộ đánh giá strategy (cache + pipeline chịu lỗi).</param>
    /// <param name="itemDurationPolicy">Chính sách thời lượng item (P-01).</param>
    /// <param name="composer">Ghép response và bản ghi audit.</param>
    /// <param name="calendar">Lịch cao điểm đưa vào <see cref="DecisionContext"/>.</param>
    /// <param name="timeProvider">Nguồn thời gian.</param>
    /// <param name="logger">Logger.</param>
    public DecisionService(
        IDecisionEvaluationStore evaluationStore,
        IStrategyRegistry strategyRegistry,
        StrategyEvaluator evaluator,
        ItemDurationPolicy itemDurationPolicy,
        DecisionResponseComposer composer,
        BranchCalendar calendar,
        TimeProvider timeProvider,
        ILogger<DecisionService> logger)
    {
        _evaluationStore = evaluationStore ?? throw new ArgumentNullException(nameof(evaluationStore));
        _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _itemDurationPolicy = itemDurationPolicy ?? throw new ArgumentNullException(nameof(itemDurationPolicy));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
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
        var registered = _strategyRegistry.ResolveActive();
        var context = new DecisionContext(nowUtc, _calendar);

        var evaluation = await _evaluator
            .EvaluateAsync(registered, reservation, context, requestHash, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

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

        _logger.LogDecisionSummary(composed.Record, evaluation.Outcome, registered.Strategy, idempotencyKey, correlationId);
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
