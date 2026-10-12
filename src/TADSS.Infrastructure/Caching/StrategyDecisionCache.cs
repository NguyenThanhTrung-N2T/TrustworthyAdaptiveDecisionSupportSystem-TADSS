using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Decisions;
using StackExchange.Redis;
using TADSS.Application.Abstractions.Caching;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Cache hai tầng của <see cref="StrategyDecision"/>: Redis (khi <c>Provider = Redis</c> và có multiplexer, qua <see cref="RedisDecisionCache"/>
/// có timeout + circuit breaker) rồi memory (khi <c>EnableFallbackToMemory</c> hoặc không dùng Redis).
/// <para>
/// Khóa đầy đủ = <c>CacheOptions.InstanceName</c> + khóa từ <see cref="StrategyDecisionCacheKey"/>, dùng cho cả Redis và memory.
/// Tầng memory lưu chính instance (bất biến); tầng Redis lưu JSON của <see cref="CachedStrategyDecision"/> theo
/// <c>TadssJson.Options</c> và dựng lại qua factory đúng trạng thái nên invariant được kiểm tra lại khi đọc.
/// TTL lấy từ tham số của lời gọi.
/// </para>
/// <para>
/// Cô lập miền lỗi: lỗi Redis, memory, serialize hoặc payload hỏng chỉ là miss (đọc) hoặc bị bỏ qua (ghi) kèm log
/// Warning; chỉ <see cref="OperationCanceledException"/> của chính caller được lan ra. Mỗi lần <see cref="GetAsync"/> ghi đúng một
/// số đo <c>tadss.cache.requests</c> (<c>hit</c>, <c>miss</c> hoặc <c>bypass</c> khi breaker Redis đang mở).
/// </para>
/// </summary>
public sealed class StrategyDecisionCache : IStrategyDecisionCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly CacheOptions _options;
    private readonly TadssMetrics _metrics;
    private readonly ILogger<StrategyDecisionCache> _logger;
    private readonly RedisDecisionCache? _redis;

    /// <summary>Khởi tạo cache.</summary>
    /// <param name="memoryCache">Cache bộ nhớ dùng chung của ứng dụng.</param>
    /// <param name="options">Cấu hình cache (<c>Provider</c>, <c>EnableFallbackToMemory</c>, <c>InstanceName</c>, tham số breaker Redis).</param>
    /// <param name="metrics">Metric của runtime (số lần tra cache).</param>
    /// <param name="timeProvider">Đồng hồ của timeout và breaker Redis.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="redis">Kết nối Redis; <c>null</c> thì chỉ dùng memory.</param>
    public StrategyDecisionCache(
        IMemoryCache memoryCache,
        IOptions<CacheOptions> options,
        TadssMetrics metrics,
        TimeProvider timeProvider,
        ILogger<StrategyDecisionCache> logger,
        IConnectionMultiplexer? redis = null)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? new CacheOptions();
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redis = redis is not null && CacheProviders.IsRedis(_options.Provider)
            ? new RedisDecisionCache(redis, _options, timeProvider, logger)
            : null;
        if (_redis is not null)
        {
            _metrics.RegisterCacheCircuitStateSource(() => _redis.CircuitState);
        }
    }

    /// <inheritdoc />
    public async Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.InstanceName + key;

        var redisStatus = RedisCacheStatus.Miss;
        if (_redis is not null)
        {
            var fromRedis = await _redis.GetAsync(fullKey, cancellationToken);
            if (fromRedis.Status == RedisCacheStatus.Hit)
            {
                _metrics.RecordCacheRequest(TadssMetricNames.CacheResultHit);
                return fromRedis.Decision;
            }

            redisStatus = fromRedis.Status;
        }

        var cached = _redis is null || _options.EnableFallbackToMemory ? ReadFromMemory(fullKey, cancellationToken) : null;
        _metrics.RecordCacheRequest(
            redisStatus == RedisCacheStatus.Bypassed ? TadssMetricNames.CacheResultBypass
            : cached is not null ? TadssMetricNames.CacheResultHit
            : TadssMetricNames.CacheResultMiss);
        return cached;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        var fullKey = _options.InstanceName + key;

        if (_redis is not null)
        {
            await _redis.SetAsync(fullKey, decision, ttl, cancellationToken);
        }

        if (_redis is null || _options.EnableFallbackToMemory)
        {
            WriteToMemory(fullKey, decision, ttl, cancellationToken);
        }
    }

    private StrategyDecision? ReadFromMemory(string fullKey, CancellationToken cancellationToken)
    {
        try
        {
            return _memoryCache.TryGetValue(fullKey, out StrategyDecision? cached) ? cached : null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Đọc memory cache thất bại cho khóa {CacheKey}; coi như miss.", fullKey);
            return null;
        }
    }

    private void WriteToMemory(string fullKey, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            _memoryCache.Set(
                fullKey,
                decision,
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl,
                    Size = 1, // Tính 1 đơn vị để không lỗi khi IMemoryCache dùng chung có SizeLimit
                });
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Ghi memory cache thất bại cho khóa {CacheKey}; bỏ qua.", fullKey);
        }
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
