using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Decorator/Wrapper chịu lỗi tự động (Resilient Hybrid Cache).
/// Ưu tiên truy xuất Redis. Khi Redis gặp sự cố (mất kết nối, timeout, exception),
/// hệ thống sẽ tự động Graceful Fallback sang In-Memory Cache mà không làm gián đoạn luồng xử lý chính của API.
/// </summary>
public class ResilientDecisionCacheService : IDecisionCacheService
{
    private readonly IDecisionCacheService? _redisCache;
    private readonly MemoryDecisionCacheService _memoryCache;
    private readonly TadssCacheOptions _options;
    private readonly ILogger<ResilientDecisionCacheService> _logger;

    public ResilientDecisionCacheService(
        MemoryDecisionCacheService memoryCache,
        IOptions<TadssCacheOptions> options,
        ILogger<ResilientDecisionCacheService> logger,
        RedisDecisionCacheService? redisCache = null)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? new TadssCacheOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redisCache = redisCache;
    }

    private bool ShouldUseRedis => string.Equals(_options.Provider, "Redis", StringComparison.OrdinalIgnoreCase) && _redisCache != null;

    public async Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (ShouldUseRedis)
        {
            try
            {
                var result = await _redisCache!.GetAsync(key, cancellationToken);
                if (result != null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Redis cache read failure for key '{CacheKey}'. Falling back to In-Memory Cache.", key);

                if (!_options.EnableFallbackToMemory)
                {
                    return null;
                }
            }
        }

        // Fallback sang InMemory Cache
        try
        {
            return await _memoryCache.GetAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "In-Memory cache read failure for key '{CacheKey}'. Proceeding as cache miss.", key);
            return null;
        }
    }

    public async Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var effectiveTtl = ttl ?? TimeSpan.FromHours(_options.TtlHours);

        if (ShouldUseRedis)
        {
            try
            {
                await _redisCache!.SetAsync(key, result, effectiveTtl, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Redis cache write failure for key '{CacheKey}'. Falling back to In-Memory Cache.", key);
            }
        }

        // Đồng thời hoặc Fallback ghi vào In-Memory Cache nếu được cấu hình
        if (_options.EnableFallbackToMemory || !ShouldUseRedis)
        {
            try
            {
                await _memoryCache.SetAsync(key, result, effectiveTtl, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "In-Memory cache write failure for key '{CacheKey}'.", key);
            }
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (ShouldUseRedis)
        {
            try
            {
                await _redisCache!.RemoveAsync(key, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Redis cache remove failure for key '{CacheKey}'.", key);
            }
        }

        try
        {
            await _memoryCache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "In-Memory cache remove failure for key '{CacheKey}'.", key);
        }
    }
}
