using Microsoft.Extensions.Logging;
using Shared.DecisionContractSDK.Decisions;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Abstractions.Resilience;
using TADSS.Application.Strategies;

namespace TADSS.Application.Decisions;

/// <summary>
/// Đánh giá một reservation bằng strategy đang kích hoạt: dựng khóa cache → đọc cache (lỗi cache chỉ là miss) → chạy pipeline khi miss
/// → ghi cache khi kết quả là <c>AUTOMATED</c> (fallback là kết quả tạm thời nên không cache). Tách khỏi <c>DecisionService</c> theo SRP.
/// <para>
/// Lỗi cache được cô lập tại đây (coi như miss / bỏ qua ghi) nên không bao giờ biến một quyết định thành công thành thất bại;
/// chỉ hủy của chính caller được lan ra. Thời gian của <c>CACHE_HIT</c> là thời gian đọc cache, đo bằng <see cref="TimeProvider"/>.
/// Singleton: chỉ phụ thuộc pipeline, cache, đồng hồ và logger (đều singleton).
/// </para>
/// </summary>
public sealed class StrategyEvaluator
{
    private readonly IStrategyPipeline _pipeline;
    private readonly IStrategyDecisionCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StrategyEvaluator> _logger;

    /// <summary>Khởi tạo bộ đánh giá strategy.</summary>
    /// <param name="pipeline">Pipeline chịu lỗi chạy strategy.</param>
    /// <param name="cache">Cache quyết định của strategy.</param>
    /// <param name="timeProvider">Nguồn thời gian (đo thời gian đọc cache).</param>
    /// <param name="logger">Logger.</param>
    public StrategyEvaluator(
        IStrategyPipeline pipeline,
        IStrategyDecisionCache cache,
        TimeProvider timeProvider,
        ILogger<StrategyEvaluator> logger)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Đánh giá <paramref name="reservation"/> bằng <paramref name="strategy"/>, qua cache khi chính sách cho phép.</summary>
    /// <param name="strategy">Strategy đang kích hoạt cùng dấu vân tay tham số.</param>
    /// <param name="reservation">Reservation đã dựng từ request.</param>
    /// <param name="context">Ngữ cảnh đánh giá (mốc <c>nowUtc</c>, lịch cao điểm).</param>
    /// <param name="requestHash">Hash chuẩn hóa của request (một phần của khóa cache).</param>
    /// <param name="idempotencyKey">Khóa idempotency, chỉ để ghi log.</param>
    /// <param name="cancellationToken">Token hủy của caller.</param>
    /// <returns>Kết quả thực thi và cờ cache hit.</returns>
    public async Task<StrategyEvaluation> EvaluateAsync(
        RegisteredStrategy strategy,
        Reservation reservation,
        DecisionContext context,
        string requestHash,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(context);

        var cachePolicy = strategy.Strategy.CachePolicy;
        var cacheKey = cachePolicy.IsEnabled
            ? TryBuildCacheKey(strategy, requestHash, idempotencyKey)
            : null;

        if (cacheKey is not null)
        {
            var readStartedAt = _timeProvider.GetTimestamp();
            var cached = await TryReadCacheAsync(cacheKey, idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                // Thời gian của CACHE_HIT là thời gian đọc cache, đo bằng TimeProvider
                var readElapsed = _timeProvider.GetElapsedTime(readStartedAt);
                var hit = new StrategyExecutionOutcome(
                    cached,
                    ExecutionType.CACHE_HIT,
                    FallbackUsed: false,
                    FailureReason: null,
                    ExecutionTimeMs: (int)Math.Max(0d, Math.Floor(readElapsed.TotalMilliseconds)));
                return new StrategyEvaluation(hit, CacheHit: true);
            }
        }

        var outcome = await _pipeline.ExecuteAsync(strategy.Strategy, reservation, context, cancellationToken).ConfigureAwait(false);

        // Chỉ kết quả do strategy tự trả (AUTOMATED) mới được cache; fallback là kết quả tạm thời
        if (cacheKey is not null
            && outcome.ExecutionType == ExecutionType.AUTOMATED
            && cachePolicy.Ttl is { } ttl)
        {
            await TryWriteCacheAsync(cacheKey, outcome.Decision, ttl, idempotencyKey, cancellationToken).ConfigureAwait(false);
        }

        return new StrategyEvaluation(outcome, CacheHit: false);
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
}
