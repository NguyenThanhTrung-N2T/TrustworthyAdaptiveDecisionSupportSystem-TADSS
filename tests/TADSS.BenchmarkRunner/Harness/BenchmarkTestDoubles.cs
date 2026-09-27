using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.DTOs.Decisions;
using Shared.DecisionContractSDK.Enums;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.BenchmarkRunner.Protocol;
using TADSS.Runtime.Api.Infrastructure.Caching;
using TADSS.Runtime.Api.Services;

namespace TADSS.BenchmarkRunner.Harness;

/// <summary>
/// Lớp cơ sở cho các chiến lược thực nghiệm có gắn bộ đếm nguyên tử (Lock-Free Atomic Telemetry Counters)
/// và tập hợp truy vết DecisionId để phân biệt chính xác 100% giữa CACHE_HIT và AUTOMATED trong kịch bản tải hỗn hợp.
/// </summary>
public abstract class InstrumentedBenchmarkStrategyBase : IReservationDecisionStrategy
{
    protected int _totalInvocations;
    protected int _retryInvocations;
    protected int _innerTimeoutCount;
    protected readonly ConcurrentDictionary<Guid, int> _attemptsByReservation = new();
    private readonly ConcurrentDictionary<Guid, byte> _evaluatedDecisionIds = new();

    public abstract string StrategyName { get; }
    public virtual string StrategyVersion => "1.0.0";

    public int TotalInvocations => Volatile.Read(ref _totalInvocations);
    public int RetryInvocations => Volatile.Read(ref _retryInvocations);
    public int InnerTimeoutCount => Volatile.Read(ref _innerTimeoutCount);

    public bool WasDecisionEvaluatedByStrategy(Guid decisionId) =>
        _evaluatedDecisionIds.ContainsKey(decisionId);

    protected DecisionResultDto TrackEvaluatedResult(DecisionResultDto result)
    {
        _evaluatedDecisionIds.TryAdd(result.DecisionId, 0);
        return result;
    }

    public virtual void ResetCounters()
    {
        Interlocked.Exchange(ref _totalInvocations, 0);
        Interlocked.Exchange(ref _retryInvocations, 0);
        Interlocked.Exchange(ref _innerTimeoutCount, 0);
        _attemptsByReservation.Clear();
        _evaluatedDecisionIds.Clear();
    }

    public abstract Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default);
}

public sealed class InstrumentedNormalStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly IReservationDecisionStrategy _inner;

    public InstrumentedNormalStrategy(IReservationDecisionStrategy inner) => _inner = inner;

    public override string StrategyName => _inner.StrategyName;
    public override string StrategyVersion => _inner.StrategyVersion;

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);
        if (attempt > 1)
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        var result = await _inner.EvaluateAsync(reservation, cancellationToken);
        return TrackEvaluatedResult(result);
    }
}

/// <summary>
/// Decorator tiêm độ trễ tính toán có kiểm soát (D0, E0, E1, E2) để mô phỏng tập quy tắc nghiệp vụ phức tạp
/// hoặc thực thi sát ngưỡng Inner Timeout (120ms).
/// </summary>
public sealed class VariableDelayBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly IReservationDecisionStrategy _inner;
    private readonly LatencyRangeMs _delayRange;

    public VariableDelayBenchmarkStrategy(IReservationDecisionStrategy inner, LatencyRangeMs delayRange)
    {
        _inner = inner;
        _delayRange = delayRange;
    }

    public override string StrategyName => _inner.StrategyName;
    public override string StrategyVersion => _inner.StrategyVersion;

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        int callSeq = Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);
        if (attempt > 1)
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        int delayMs = _delayRange.ComputeDeterministicDelayMs(callSeq);
        if (delayMs > 0)
        {
            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _innerTimeoutCount);
                throw;
            }
        }

        var result = await _inner.EvaluateAsync(reservation, cancellationToken);
        return TrackEvaluatedResult(result);
    }
}

/// <summary>
/// Chiến lược mô phỏng biến thiên độ trễ hai đỉnh (Bimodal Latency Jitter - Kịch bản D2):
/// 80% request thực thi nhanh (5ms), 20% request thực thi chậm (95ms).
/// </summary>
public sealed class BimodalJitterBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly IReservationDecisionStrategy _inner;
    private readonly int _fastDelayMs;
    private readonly int _slowDelayMs;
    private readonly int _slowRequestModulo;

    public BimodalJitterBenchmarkStrategy(
        IReservationDecisionStrategy inner,
        int fastDelayMs,
        int slowDelayMs,
        int slowRequestModulo)
    {
        _inner = inner;
        _fastDelayMs = fastDelayMs;
        _slowDelayMs = slowDelayMs;
        _slowRequestModulo = Math.Max(1, slowRequestModulo);
    }

    public override string StrategyName => _inner.StrategyName;
    public override string StrategyVersion => _inner.StrategyVersion;

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        int callSeq = Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);
        if (attempt > 1)
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        int delayMs = (callSeq % _slowRequestModulo == 0) ? _slowDelayMs : _fastDelayMs;
        try
        {
            await Task.Delay(delayMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _innerTimeoutCount);
            throw;
        }

        var result = await _inner.EvaluateAsync(reservation, cancellationToken);
        return TrackEvaluatedResult(result);
    }
}

/// <summary>
/// Chiến lược mô phỏng lỗi thoáng qua ngắt quãng (Intermittent Transient Failures - Kịch bản E3):
/// Cứ mỗi N request thì lần thử đầu tiên (Attempt 1) ném ngoại lệ tạm thời, sau đó phục hồi ở Attempt 2 nhờ Polly Retry.
/// </summary>
public sealed class IntermittentFaultBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly IReservationDecisionStrategy _inner;
    private readonly int _failureModulo;
    private int _uniqueReservationCounter;

    public IntermittentFaultBenchmarkStrategy(IReservationDecisionStrategy inner, int failureModulo)
    {
        _inner = inner;
        _failureModulo = Math.Max(1, failureModulo);
    }

    public override string StrategyName => _inner.StrategyName;
    public override string StrategyVersion => _inner.StrategyVersion;

    public override void ResetCounters()
    {
        base.ResetCounters();
        Interlocked.Exchange(ref _uniqueReservationCounter, 0);
    }

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);

        if (attempt == 1)
        {
            int seq = Interlocked.Increment(ref _uniqueReservationCounter);
            if (seq % _failureModulo == 0)
            {
                throw new InvalidOperationException("Simulated intermittent transient fault on first attempt.");
            }
        }
        else
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        var result = await _inner.EvaluateAsync(reservation, cancellationToken);
        return TrackEvaluatedResult(result);
    }
}

/// <summary>
/// Chiến lược mô phỏng quá trình tự phục hồi mạch ngắt (Circuit Breaker Half-Open -> Closed Recovery - Kịch bản E4):
/// Gây lỗi cạn kiệt trong pha Warm-up để mở mạch (OPEN), sau đó chuyển sang trạng thái khỏe mạnh trong pha Measurement
/// để request thăm dò (Half-Open Probe) đóng mạch (CLOSED) thành công.
/// </summary>
public sealed class RecoveringCircuitBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly IReservationDecisionStrategy _inner;
    private readonly int _tripDelayMs;
    private volatile bool _isHealthyRecoveryPhase;

    public RecoveringCircuitBenchmarkStrategy(IReservationDecisionStrategy inner, int tripDelayMs)
    {
        _inner = inner;
        _tripDelayMs = tripDelayMs;
    }

    public override string StrategyName => _inner.StrategyName;
    public override string StrategyVersion => _inner.StrategyVersion;

    public void EnterHealthyRecoveryPhase() => _isHealthyRecoveryPhase = true;

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);
        if (attempt > 1)
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        if (!_isHealthyRecoveryPhase)
        {
            try
            {
                await Task.Delay(_tripDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _innerTimeoutCount);
                throw;
            }
        }

        var result = await _inner.EvaluateAsync(reservation, cancellationToken);
        return TrackEvaluatedResult(result);
    }
}

public sealed class RecoverableTimeoutBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly int _firstAttemptDelayMs;

    public RecoverableTimeoutBenchmarkStrategy(int firstAttemptDelayMs)
    {
        _firstAttemptDelayMs = firstAttemptDelayMs;
    }

    public override string StrategyName => "DefaultDecisionStrategy";

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);

        if (attempt == 1)
        {
            try
            {
                await Task.Delay(_firstAttemptDelayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _innerTimeoutCount);
                throw;
            }
        }
        else
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = reservation.ReservationId,
            Status = DecisionStatus.APPROVED.ToString(),
            StrategyName = StrategyName,
            StrategyVersion = StrategyVersion,
            ExecutionTimeMs = 1,
            FallbackUsed = false,
            PrimaryReason = "RETRY_RECOVERED",
            Reasons = ["Phục hồi thành công ở lần thử lại thứ 2"],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        return TrackEvaluatedResult(result);
    }
}

public sealed class ExhaustiveTimeoutBenchmarkStrategy : InstrumentedBenchmarkStrategyBase
{
    private readonly int _delayMs;

    public ExhaustiveTimeoutBenchmarkStrategy(int delayMs)
    {
        _delayMs = delayMs;
    }

    public override string StrategyName => "DefaultDecisionStrategy";

    public override async Task<DecisionResultDto> EvaluateAsync(
        Reservation reservation,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalInvocations);
        var attempt = _attemptsByReservation.AddOrUpdate(reservation.ReservationId, 1, (_, c) => c + 1);
        if (attempt > 1)
        {
            Interlocked.Increment(ref _retryInvocations);
        }

        try
        {
            await Task.Delay(_delayMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _innerTimeoutCount);
            throw;
        }

        var result = new DecisionResultDto
        {
            DecisionId = Guid.NewGuid(),
            ReservationId = reservation.ReservationId,
            Status = DecisionStatus.APPROVED.ToString(),
            StrategyName = StrategyName,
            StrategyVersion = StrategyVersion,
            ExecutionTimeMs = _delayMs,
            FallbackUsed = false,
            PrimaryReason = "UNREACHABLE",
            Reasons = [],
            Recommendations = [],
            EvaluatedAt = DateTime.UtcNow
        };

        return TrackEvaluatedResult(result);
    }
}

public sealed class SingleStrategyResolver : IStrategyResolver
{
    private readonly IReservationDecisionStrategy _strategy;

    public SingleStrategyResolver(IReservationDecisionStrategy strategy) => _strategy = strategy;

    public IReservationDecisionStrategy ResolveActive() => _strategy;
    public IReservationDecisionStrategy ResolveByName(string strategyName) => _strategy;
    public IReadOnlyList<StrategyRegistration> ListRegistered() =>
        [new StrategyRegistration(_strategy.StrategyName, _strategy.StrategyVersion)];
}

public interface IBenchmarkResettableCache : IDecisionCacheService
{
    void ClearStore();
}

public sealed class NoOpDecisionCacheService : IBenchmarkResettableCache
{
    public Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<DecisionResultDto?>(null);

    public Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void ClearStore()
    {
    }
}

public sealed class SerializedInProcessCacheService : IBenchmarkResettableCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_store.TryGetValue($"tadss:{key}", out var json))
        {
            var dto = JsonSerializer.Deserialize<DecisionResultDto>(json, JsonOptions);
            return Task.FromResult(dto);
        }

        return Task.FromResult<DecisionResultDto?>(null);
    }

    public Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(result, JsonOptions);
        _store[$"tadss:{key}"] = json;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryRemove($"tadss:{key}", out _);
        return Task.CompletedTask;
    }

    public void ClearStore() => _store.Clear();
}

/// <summary>
/// Decorator giả lập độ trễ mạng WAN cho mỗi thao tác đọc/ghi Redis (Kịch bản C1, E1, E2).
/// Chỉ kích hoạt độ trễ trong pha Measurement để pha Warm-up khởi chạy nhanh.
/// </summary>
public sealed class NetworkLatencyCacheDecorator : IBenchmarkResettableCache
{
    private readonly IBenchmarkResettableCache _inner;
    private readonly LatencyRangeMs _wanLatencyRange;
    private int _operationCounter;
    private volatile bool _enableLatencySimulation;

    public NetworkLatencyCacheDecorator(IBenchmarkResettableCache inner, LatencyRangeMs wanLatencyRange)
    {
        _inner = inner;
        _wanLatencyRange = wanLatencyRange;
    }

    public void EnableMeasurementLatency(bool enabled) => _enableLatencySimulation = enabled;

    public async Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await SimulateWanLatencyIfActiveAsync(cancellationToken);
        return await _inner.GetAsync(key, cancellationToken);
    }

    public async Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        await SimulateWanLatencyIfActiveAsync(cancellationToken);
        await _inner.SetAsync(key, result, ttl, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => _inner.RemoveAsync(key, cancellationToken);

    public void ClearStore() => _inner.ClearStore();

    private async Task SimulateWanLatencyIfActiveAsync(CancellationToken cancellationToken)
    {
        if (!_enableLatencySimulation || !_wanLatencyRange.IsEnabled)
        {
            return;
        }

        int seq = Interlocked.Increment(ref _operationCounter);
        int delayMs = _wanLatencyRange.ComputeDeterministicDelayMs(seq);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken);
        }
    }
}

/// <summary>
/// Bộ nhớ đệm giới hạn dung lượng (Bounded Capacity Cache) có cơ chế trục xuất FIFO/LRU khi đầy (Kịch bản C3).
/// </summary>
public sealed class BoundedCapacityCacheService : IBenchmarkResettableCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly int _maxCapacity;
    private readonly object _syncLock = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, string Json)>> _map;
    private readonly LinkedList<(string Key, string Json)> _lruList = new();

    public BoundedCapacityCacheService(int maxCapacity)
    {
        _maxCapacity = Math.Max(1, maxCapacity);
        _map = new Dictionary<string, LinkedListNode<(string Key, string Json)>>(_maxCapacity, StringComparer.Ordinal);
    }

    public Task<DecisionResultDto?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullKey = $"tadss:{key}";
        string? json = null;

        lock (_syncLock)
        {
            if (_map.TryGetValue(fullKey, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                json = node.Value.Json;
            }
        }

        if (json != null)
        {
            return Task.FromResult(JsonSerializer.Deserialize<DecisionResultDto>(json, JsonOptions));
        }

        return Task.FromResult<DecisionResultDto?>(null);
    }

    public Task SetAsync(string key, DecisionResultDto result, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullKey = $"tadss:{key}";
        string json = JsonSerializer.Serialize(result, JsonOptions);

        lock (_syncLock)
        {
            if (_map.TryGetValue(fullKey, out var existingNode))
            {
                _lruList.Remove(existingNode);
                var updatedNode = new LinkedListNode<(string Key, string Json)>((fullKey, json));
                _lruList.AddFirst(updatedNode);
                _map[fullKey] = updatedNode;
            }
            else
            {
                if (_map.Count >= _maxCapacity && _lruList.Last != null)
                {
                    var evicted = _lruList.Last.Value;
                    _lruList.RemoveLast();
                    _map.Remove(evicted.Key);
                }

                var newNode = new LinkedListNode<(string Key, string Json)>((fullKey, json));
                _lruList.AddFirst(newNode);
                _map[fullKey] = newNode;
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullKey = $"tadss:{key}";
        lock (_syncLock)
        {
            if (_map.TryGetValue(fullKey, out var node))
            {
                _lruList.Remove(node);
                _map.Remove(fullKey);
            }
        }

        return Task.CompletedTask;
    }

    public void ClearStore()
    {
        lock (_syncLock)
        {
            _map.Clear();
            _lruList.Clear();
        }
    }
}

/// <summary>
/// EF Core SaveChangesInterceptor giả lập độ trễ tranh chấp ghi Audit đồng bộ xuống PostgreSQL (Kịch bản D1, E0, E2).
/// </summary>
public sealed class SimulatedDbContentionInterceptor : SaveChangesInterceptor
{
    private readonly LatencyRangeMs _writeDelayRange;
    private int _writeSequence;
    private volatile bool _enableContentionSimulation;

    public SimulatedDbContentionInterceptor(LatencyRangeMs writeDelayRange)
    {
        _writeDelayRange = writeDelayRange;
    }

    public void EnableMeasurementContention(bool enabled) => _enableContentionSimulation = enabled;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_enableContentionSimulation && _writeDelayRange.IsEnabled)
        {
            int seq = Interlocked.Increment(ref _writeSequence);
            int delayMs = _writeDelayRange.ComputeDeterministicDelayMs(seq);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
