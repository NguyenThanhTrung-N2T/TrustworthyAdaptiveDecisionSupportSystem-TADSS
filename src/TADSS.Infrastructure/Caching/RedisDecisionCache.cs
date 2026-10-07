using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Decisions;
using StackExchange.Redis;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Resilience;

namespace TADSS.Infrastructure.Caching;

/// <summary>
/// Tầng Redis của cache quyết định: mọi lệnh chạy trong pipeline riêng (timeout + circuit breaker, <see cref="RedisCacheResilience"/>).
/// StackExchange.Redis không nhận <see cref="CancellationToken"/> nên lệnh được chờ bằng <c>WaitAsync(token)</c> để timeout cắt được.
/// <para>
/// Lỗi Redis và quá hạn trả <see cref="RedisCacheStatus.Failed"/> (log Warning chỉ kèm kiểu exception vì message có thể chứa chuỗi kết nối);
/// breaker mở trả <see cref="RedisCacheStatus.Bypassed"/> mà không gọi Redis; payload hỏng là <see cref="RedisCacheStatus.Miss"/> và không
/// tính vào breaker. Chỉ hủy của chính caller được lan ra. Trạng thái breaker theo từng instance.
/// </para>
/// </summary>
internal sealed class RedisDecisionCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ResiliencePipeline _pipeline;
    private readonly CircuitBreakerStateProvider _stateProvider = new();
    private readonly ILogger _logger;

    /// <summary>Khởi tạo tầng Redis.</summary>
    /// <param name="redis">Kết nối Redis.</param>
    /// <param name="options">Cấu hình cache (timeout và tham số breaker).</param>
    /// <param name="timeProvider">Đồng hồ của timeout và breaker.</param>
    /// <param name="logger">Logger.</param>
    public RedisDecisionCache(IConnectionMultiplexer redis, CacheOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeline = RedisCacheResilience.Create(options, timeProvider, logger, _stateProvider);
    }

    /// <summary>Trạng thái hiện tại của circuit breaker Redis (cho gauge).</summary>
    public CircuitStateValue CircuitState => CircuitStateMapper.ToValue(_stateProvider.CircuitState);

    /// <summary>Đọc quyết định theo khóa đầy đủ (đã gồm <c>InstanceName</c>); span <c>GET</c> chỉ ghi kết quả, không ghi khóa.</summary>
    public async Task<RedisReadResult> GetAsync(string fullKey, CancellationToken cancellationToken)
    {
        using var activity = StartCommandActivity(TadssActivitySource.DbOperationGet);
        var result = await ReadAsync(fullKey, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(TadssActivitySource.TagCacheResult, result.Status.ToString());
        return result;
    }

    /// <summary>Ghi quyết định theo khóa đầy đủ với TTL <paramref name="ttl"/>; span <c>SET</c> chỉ ghi kết quả, không ghi khóa.</summary>
    public async Task<RedisCacheStatus> SetAsync(string fullKey, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        using var activity = StartCommandActivity(TadssActivitySource.DbOperationSet);
        var status = await WriteAsync(fullKey, decision, ttl, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(TadssActivitySource.TagCacheResult, status.ToString());
        return status;
    }

    private static Activity? StartCommandActivity(string operation)
    {
        var activity = TadssActivitySource.Source.StartActivity(TadssActivitySource.RedisCommand, ActivityKind.Client);
        activity?.SetTag(TadssActivitySource.TagDbSystem, TadssActivitySource.DbSystemRedis);
        activity?.SetTag(TadssActivitySource.TagDbOperation, operation);
        return activity;
    }

    private async Task<RedisReadResult> ReadAsync(string fullKey, CancellationToken cancellationToken)
    {
        RedisValue value;
        try
        {
            value = await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().StringGetAsync(fullKey).WaitAsync(token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }
        catch (BrokenCircuitException)
        {
            return new RedisReadResult(RedisCacheStatus.Bypassed, null);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning("Đọc Redis cache thất bại cho khóa {CacheKey} ({ExceptionType}); coi như miss.", fullKey, ex.GetType().Name);
            return new RedisReadResult(RedisCacheStatus.Failed, null);
        }

        if (value.IsNullOrEmpty)
        {
            return new RedisReadResult(RedisCacheStatus.Miss, null);
        }

        try
        {
            var cached = JsonSerializer.Deserialize<CachedStrategyDecision>(value.ToString(), TadssJson.Options)
                ?? throw new InvalidDataException("Payload là JSON null.");
            return new RedisReadResult(RedisCacheStatus.Hit, cached.ToDecision());
        }
        catch (Exception ex)
        {
            // Payload hỏng/không còn hợp lệ: coi như miss; lần Set kế tiếp sẽ ghi đè. Không tính vào breaker vì Redis vẫn khỏe
            _logger.LogWarning(ex, "Payload Redis cache hỏng cho khóa {CacheKey}; coi như miss.", fullKey);
            return new RedisReadResult(RedisCacheStatus.Miss, null);
        }
    }

    private async Task<RedisCacheStatus> WriteAsync(string fullKey, StrategyDecision decision, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(CachedStrategyDecision.From(decision), TadssJson.Options);
            await _pipeline.ExecuteAsync(
                async token => await _redis.GetDatabase().StringSetAsync(fullKey, json, ttl, When.Always, CommandFlags.None).WaitAsync(token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            return RedisCacheStatus.Written;
        }
        catch (BrokenCircuitException)
        {
            return RedisCacheStatus.Bypassed;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            _logger.LogWarning("Ghi Redis cache thất bại cho khóa {CacheKey} ({ExceptionType}); bỏ qua.", fullKey, ex.GetType().Name);
            return RedisCacheStatus.Failed;
        }
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
