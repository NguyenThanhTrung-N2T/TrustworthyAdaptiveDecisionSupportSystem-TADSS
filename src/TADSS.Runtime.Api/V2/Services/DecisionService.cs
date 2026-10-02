using System.Text.Json;
using Shared.DecisionContractSDK.V2.Common;
using Shared.DecisionContractSDK.V2.Decisions;
using Shared.DecisionContractSDK.V2.Domain;
using Shared.DecisionContractSDK.V2.DTOs.Decisions;
using Shared.DecisionContractSDK.V2.DTOs.Mappings;
using Shared.DecisionContractSDK.V2.Enums;
using Shared.DecisionContractSDK.V2.Interfaces;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.V2.Infrastructure.Caching;
using TADSS.Runtime.Api.V2.Infrastructure.Data;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Services.Decisions;
using TADSS.Runtime.Api.V2.Services.Strategies;

namespace TADSS.Runtime.Api.V2.Services;

/// <summary>
/// Hiện thực <see cref="IDecisionService"/> theo luồng spec 4.2.3: kiểm tra khóa → dựng domain → hash → tra idempotency
/// → kiểm tra thời lượng item (P-01) → chọn strategy → cache → pipeline → ghi cache (chỉ <c>AUTOMATED</c>) → approval
/// window và cọc → ghi audit đồng bộ (fail-closed, INV-10) → response.
/// <para>
/// Một mốc <c>nowUtc</c> duy nhất lấy từ <see cref="TimeProvider"/>, cắt phần dưới micro giây, cho cả lần đánh giá (B-03, C-08). Lỗi trước bước ghi audit
/// (validation, P-01, xung đột idempotency) không ghi audit và không chạy strategy. Lỗi cache được cô lập tại đây
/// (coi như miss / bỏ qua ghi) nên không bao giờ biến một quyết định thành công thành thất bại; chỉ hủy của chính
/// caller được lan ra. Không đăng ký DI ở gói 5.2b (B-02); gói 5.2c đăng ký cùng <c>EfDecisionAuditStore</c>.
/// </para>
/// </summary>
public sealed class DecisionService : IDecisionService
{
    private const int MaxIdempotencyKeyLength = 128;

    private readonly IDecisionAuditStore _auditStore;
    private readonly IStrategyRegistry _strategyRegistry;
    private readonly IStrategyPipeline _pipeline;
    private readonly IStrategyDecisionCache _cache;
    private readonly ItemDurationPolicy _itemDurationPolicy;
    private readonly DecisionResponseComposer _composer;
    private readonly BranchCalendar _calendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionService> _logger;

    /// <summary>Khởi tạo service điều phối.</summary>
    /// <param name="auditStore">Kho audit (tra idempotency, ghi bản ghi <c>PRIMARY</c>).</param>
    /// <param name="strategyRegistry">Danh bạ strategy v2.</param>
    /// <param name="pipeline">Pipeline chịu lỗi chạy strategy.</param>
    /// <param name="cache">Cache quyết định của strategy.</param>
    /// <param name="itemDurationPolicy">Chính sách thời lượng item (P-01).</param>
    /// <param name="composer">Ghép response và bản ghi audit.</param>
    /// <param name="calendar">Lịch cao điểm đưa vào <see cref="DecisionContext"/>.</param>
    /// <param name="timeProvider">Nguồn thời gian.</param>
    /// <param name="logger">Logger.</param>
    public DecisionService(
        IDecisionAuditStore auditStore,
        IStrategyRegistry strategyRegistry,
        IStrategyPipeline pipeline,
        IStrategyDecisionCache cache,
        ItemDurationPolicy itemDurationPolicy,
        DecisionResponseComposer composer,
        BranchCalendar calendar,
        TimeProvider timeProvider,
        ILogger<DecisionService> logger)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _strategyRegistry = strategyRegistry ?? throw new ArgumentNullException(nameof(strategyRegistry));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

        var existing = await _auditStore.FindPrimaryByIdempotencyKeyAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
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

        var nowUtc = TruncateToMicroseconds(_timeProvider.GetUtcNow());
        var registered = _strategyRegistry.ResolveActive();
        var strategy = registered.Strategy;
        var context = new DecisionContext(nowUtc, _calendar);

        var cachePolicy = strategy.CachePolicy;
        var cacheKey = cachePolicy.IsEnabled
            ? TryBuildCacheKey(registered, requestHash, idempotencyKey)
            : null;

        StrategyExecutionOutcome? outcome = null;
        var cacheHit = false;

        if (cacheKey is not null)
        {
            var readStartedAt = _timeProvider.GetTimestamp();
            var cached = await TryReadCacheAsync(cacheKey, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                // Thời gian của CACHE_HIT là thời gian đọc cache, đo bằng TimeProvider
                var readElapsed = _timeProvider.GetElapsedTime(readStartedAt);
                outcome = new StrategyExecutionOutcome(
                    cached,
                    ExecutionType.CACHE_HIT,
                    FallbackUsed: false,
                    FailureReason: null,
                    ExecutionTimeMs: (int)Math.Max(0d, Math.Floor(readElapsed.TotalMilliseconds)));
                cacheHit = true;
            }
        }

        if (outcome is null)
        {
            outcome = await _pipeline.ExecuteAsync(strategy, reservation, context, cancellationToken).ConfigureAwait(false);

            // Chỉ kết quả do strategy tự trả (AUTOMATED) mới được cache; fallback là kết quả tạm thời
            if (cacheKey is not null
                && outcome.ExecutionType == ExecutionType.AUTOMATED
                && cachePolicy.Ttl is { } ttl)
            {
                await TryWriteCacheAsync(cacheKey, outcome.Decision, ttl, idempotencyKey, cancellationToken).ConfigureAwait(false);
            }
        }

        var composed = _composer.Compose(new DecisionComposition(
            DecisionId: Guid.NewGuid(),
            Request: request,
            Reservation: reservation,
            Strategy: registered,
            Outcome: outcome,
            CacheHit: cacheHit,
            NowUtc: nowUtc,
            RequestHash: requestHash,
            IdempotencyKey: idempotencyKey,
            CorrelationId: correlationId));

        // Ghi audit đồng bộ, fail-closed (INV-10): lỗi lan ra và không có response nào được trả
        var inserted = await _auditStore.InsertExecutionAsync(composed.Record, cancellationToken).ConfigureAwait(false);
        if (inserted.IsExistingWinner)
        {
            var winner = ReplayStored(inserted.Stored, requestHash, idempotencyKey);
            _logger.LogInformation(
                "Tranh chấp idempotency đã được giải quyết cho khóa {IdempotencyKey}: trả quyết định {DecisionId} của request thắng.",
                idempotencyKey,
                winner.DecisionId);
            return winner;
        }

        LogDecision(composed.Record, outcome, strategy, idempotencyKey, correlationId);
        return composed.Response;
    }

    /// <summary>
    /// Cắt phần dưới micro giây (bội số 10 tick) để mốc <c>nowUtc</c> khớp độ chính xác của cột <c>timestamptz</c>
    /// và response replay/audit trùng từng tick với response gốc (B-03, C-08).
    /// </summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }

    private static void EnsureValidIdempotencyKey(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new ArgumentException(
                $"Idempotency key không được dài quá {MaxIdempotencyKeyLength} ký tự.",
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

        return JsonSerializer.Deserialize<DecisionResultDto>(stored.ResponsePayload, TadssJson.Options)
            ?? throw new InvalidOperationException("Response đã lưu của quyết định không đọc được.");
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private string? TryBuildCacheKey(RegisteredStrategy registered, string requestHash, string idempotencyKey)
    {
        try
        {
            return StrategyDecisionCacheKey.Build(
                registered.Strategy.StrategyName,
                registered.Strategy.StrategyVersion,
                registered.OptionsFingerprint,
                requestHash);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "Không dựng được khóa cache cho strategy {StrategyName}; bỏ qua cache. IdempotencyKey {IdempotencyKey}.",
                registered.Strategy.StrategyName,
                idempotencyKey);
            return null;
        }
    }

    private async Task<StrategyDecision?> TryReadCacheAsync(string cacheKey, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            // Cô lập miền lỗi (RESILIENCE_AND_FAULT_TAXONOMY, nguyên tắc 2): lỗi cache chỉ là miss
            _logger.LogWarning(ex, "Đọc cache quyết định thất bại; coi như miss. IdempotencyKey {IdempotencyKey}.", idempotencyKey);
            return null;
        }
    }

    private async Task TryWriteCacheAsync(
        string cacheKey,
        StrategyDecision decision,
        TimeSpan ttl,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(cacheKey, decision, ttl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Ghi cache quyết định thất bại; bỏ qua. IdempotencyKey {IdempotencyKey}.", idempotencyKey);
        }
    }

    private void LogDecision(
        DecisionExecutionRecord record,
        StrategyExecutionOutcome outcome,
        IReservationDecisionStrategy strategy,
        string idempotencyKey,
        string? correlationId)
    {
        // B-05: approval window không khả dụng → ghi reasonCode gốc của strategy (response chỉ còn lý do cố định)
        if (record.StrategyStatus == DecisionStatus.NEED_APPROVAL && record.Status == DecisionStatus.APPROVED)
        {
            _logger.LogInformation(
                "Approval window không khả dụng cho quyết định {DecisionId}: strategy {StrategyName} trả {StrategyStatus} với reasonCode gốc {StrategyReasonCode}; chuyển APPROVED cọc 100%. IdempotencyKey {IdempotencyKey}.",
                record.DecisionId,
                strategy.StrategyName,
                record.StrategyStatus,
                outcome.Decision.ReasonCode,
                idempotencyKey);
        }

        _logger.LogInformation(
            "Quyết định {DecisionId} cho reservation {ReservationId}: {Status} ({ReasonCode}), strategy {StrategyName} v{StrategyVersion}, executionType {ExecutionType}, fallbackUsed {FallbackUsed}, {ElapsedMs} ms, idempotencyKey {IdempotencyKey}, correlationId {CorrelationId}.",
            record.DecisionId,
            record.ReservationId,
            record.Status,
            record.ReasonCode,
            record.StrategyName,
            record.StrategyVersion,
            record.ExecutionType,
            record.FallbackUsed,
            record.ExecutionTimeMs,
            idempotencyKey,
            correlationId);
    }
}
