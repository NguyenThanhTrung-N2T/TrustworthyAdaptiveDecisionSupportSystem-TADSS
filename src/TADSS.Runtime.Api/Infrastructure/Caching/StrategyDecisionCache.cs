using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using StackExchange.Redis;
using TADSS.Runtime.Api.Infrastructure.Caching;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Cache hai tầng của <see cref="StrategyDecision"/>: Redis (khi <c>Provider = Redis</c> và có multiplexer) rồi memory
/// (khi <c>EnableFallbackToMemory</c> hoặc không dùng Redis).
/// <para>
/// Tầng memory lưu chính instance (bất biến); tầng Redis lưu JSON của <see cref="CachedStrategyDecision"/> theo
/// <see cref="TadssJson.Options"/> và dựng lại qua factory đúng trạng thái nên invariant được kiểm tra lại khi đọc.
/// TTL lấy từ tham số của lời gọi (không dùng <c>TtlHours</c>); khóa dùng nguyên văn (không cộng <c>InstanceName</c>).
/// </para>
/// <para>
/// Cô lập miền lỗi: lỗi Redis, memory, serialize hoặc payload hỏng chỉ là miss (đọc) hoặc bị bỏ qua (ghi) kèm log
/// Warning; chỉ <see cref="OperationCanceledException"/> của chính caller được lan ra. Circuit breaker riêng cho Redis
/// và metric <c>bypass</c> thuộc gói 5.4.
/// </para>
/// </summary>
public sealed class StrategyDecisionCache : IStrategyDecisionCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly TadssCacheOptions _options;
    private readonly ILogger<StrategyDecisionCache> _logger;
    private readonly IConnectionMultiplexer? _redis;

    /// <summary>Khởi tạo cache.</summary>
    /// <param name="memoryCache">Cache bộ nhớ dùng chung của ứng dụng.</param>
    /// <param name="options">Cấu hình cache (<c>Provider</c>, <c>EnableFallbackToMemory</c>).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="redis">Kết nối Redis; <c>null</c> thì chỉ dùng memory.</param>
    public StrategyDecisionCache(
        IMemoryCache memoryCache,
        IOptions<TadssCacheOptions> options,
        ILogger<StrategyDecisionCache> logger,
        IConnectionMultiplexer? redis = null)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? new TadssCacheOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redis = redis;
    }

    private bool ShouldUseRedis =>
        _redis is not null && string.Equals(_options.Provider, "Redis", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<StrategyDecision?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        if (_redis is not null && ShouldUseRedis)
        {
            try
            {
                var fromRedis = await ReadFromRedisAsync(_redis, key);
                if (fromRedis is not null || !_options.EnableFallbackToMemory)
                {
                    return fromRedis;
                }
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                _logger.LogWarning(ex, "Đọc Redis cache v2 thất bại cho khóa {CacheKey}; coi như miss.", key);

                if (!_options.EnableFallbackToMemory)
                {
                    return null;
                }
            }
        }

        try
        {
            return _memoryCache.TryGetValue(key, out StrategyDecision? cached) ? cached : null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Đọc memory cache v2 thất bại cho khóa {CacheKey}; coi như miss.", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        if (_redis is not null && ShouldUseRedis)
        {
            try
            {
                var json = JsonSerializer.Serialize(CachedStrategyDecision.From(decision), TadssJson.Options);
                await _redis.GetDatabase().StringSetAsync(key, json, ttl, When.Always, CommandFlags.None);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                _logger.LogWarning(ex, "Ghi Redis cache v2 thất bại cho khóa {CacheKey}; bỏ qua.", key);
            }
        }

        if (_options.EnableFallbackToMemory || !ShouldUseRedis)
        {
            try
            {
                _memoryCache.Set(
                    key,
                    decision,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = ttl,
                        Size = 1, // Tính 1 đơn vị để không lỗi khi IMemoryCache dùng chung có SizeLimit
                    });
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                _logger.LogWarning(ex, "Ghi memory cache v2 thất bại cho khóa {CacheKey}; bỏ qua.", key);
            }
        }
    }

    private async Task<StrategyDecision?> ReadFromRedisAsync(IConnectionMultiplexer redis, string key)
    {
        var value = await redis.GetDatabase().StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var cached = JsonSerializer.Deserialize<CachedStrategyDecision>(value.ToString(), TadssJson.Options)
                ?? throw new InvalidDataException("Payload là JSON null.");
            return cached.ToDecision();
        }
        catch (Exception ex)
        {
            // Payload hỏng/không còn hợp lệ: coi như miss; lần Set kế tiếp sẽ ghi đè
            _logger.LogWarning(ex, "Payload Redis cache v2 hỏng cho khóa {CacheKey}; coi như miss.", key);
            return null;
        }
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
