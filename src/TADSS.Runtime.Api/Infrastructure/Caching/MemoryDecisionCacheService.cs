using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.DTOs.Decisions;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Triển khai Cache trên bộ nhớ cục bộ (In-Memory Cache) với cơ chế giới hạn kích thước và LRU Eviction.
/// Phục vụ chế độ Local Cache độc lập hoặc làm Fallback dự phòng khi Redis gặp sự cố.
/// </summary>
public class MemoryDecisionCacheService : IDecisionCacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly TadssCacheOptions _options;
    private readonly ILogger<MemoryDecisionCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MemoryDecisionCacheService(
        IMemoryCache memoryCache,
        IOptions<TadssCacheOptions> options,
        ILogger<MemoryDecisionCacheService> logger)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? new TadssCacheOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_memoryCache.TryGetValue(key, out string? cachedJson) && !string.IsNullOrWhiteSpace(cachedJson))
        {
            try
            {
                var result = JsonSerializer.Deserialize<DecisionResultDto>(cachedJson, JsonOptions);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cached JSON payload for key {CacheKey}.", key);
                _memoryCache.Remove(key);
            }
        }

        return Task.FromResult<DecisionResultDto?>(null);
    }

    public Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(result);

        var effectiveTtl = ttl ?? TimeSpan.FromHours(_options.TtlHours);
        var json = JsonSerializer.Serialize(result, JsonOptions);

        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = effectiveTtl,
            Size = 1 // Hỗ trợ định cỡ cache cho SizeLimit
        };

        _memoryCache.Set(key, json, entryOptions);
        _logger.LogDebug("Decision cached in Memory with key {CacheKey} (TTL: {Ttl}).", key, effectiveTtl);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _memoryCache.Remove(key);
        return Task.CompletedTask;
    }
}
