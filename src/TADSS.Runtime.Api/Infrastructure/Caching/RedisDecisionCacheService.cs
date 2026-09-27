using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.DTOs.Decisions;
using StackExchange.Redis;

namespace TADSS.Runtime.Api.Infrastructure.Caching;

/// <summary>
/// Triển khai Distributed Caching trên Redis Server sử dụng StackExchange.Redis.
/// </summary>
public class RedisDecisionCacheService : IDecisionCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TadssCacheOptions _options;
    private readonly ILogger<RedisDecisionCacheService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisDecisionCacheService(
        IConnectionMultiplexer redis,
        IOptions<TadssCacheOptions> options,
        ILogger<RedisDecisionCacheService> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options?.Value ?? new TadssCacheOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private IDatabase Database => _redis.GetDatabase();

    private string FormatKey(string key) => $"{_options.InstanceName}{key}";

    public async Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = FormatKey(key);
        var redisValue = await Database.StringGetAsync(redisKey);

        if (redisValue.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DecisionResultDto>(redisValue.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Redis cached payload for key {RedisKey}.", redisKey);
            await Database.KeyDeleteAsync(redisKey);
            return null;
        }
    }

    public async Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = FormatKey(key);
        var effectiveTtl = ttl ?? TimeSpan.FromHours(_options.TtlHours);
        var json = JsonSerializer.Serialize(result, JsonOptions);

        await Database.StringSetAsync(redisKey, json, effectiveTtl);
        _logger.LogDebug("Decision cached in Redis with key {RedisKey} (TTL: {Ttl}).", redisKey, effectiveTtl);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = FormatKey(key);
        await Database.KeyDeleteAsync(redisKey);
    }
}
