using Shared.DecisionContractSDK.Decisions;
using TADSS.Runtime.Api.Services.Strategies;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Double của <see cref="IStrategyRegistry"/> chỉ dùng trong host test (C-10): bọc registry thật, trả mọi strategy dưới dạng
/// <see cref="ProbeStrategy"/> (cùng fingerprint tham số) để test đếm số lần strategy chạy và ghi đè chính sách cache.
/// Không có gì ở mã Production.
/// </summary>
public sealed class ProbeStrategyRegistry : IStrategyRegistry
{
    private readonly IStrategyRegistry _inner;
    private readonly List<RegisteredStrategy> _all;
    private int _invocationCount;

    /// <summary>Bọc <paramref name="inner"/>.</summary>
    public ProbeStrategyRegistry(IStrategyRegistry inner)
    {
        _inner = inner;
        _all = inner.All
            .Select(registered => new RegisteredStrategy(new ProbeStrategy(registered.Strategy, this), registered.OptionsFingerprint))
            .ToList();
    }

    /// <summary>Số lần một strategy (bất kỳ) đã được gọi <c>EvaluateAsync</c>.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    /// <summary>Chính sách cache thay cho chính sách của strategy thật; <c>null</c> thì giữ chính sách thật.</summary>
    public StrategyCachePolicy? CachePolicyOverride { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All => _all;

    /// <inheritdoc />
    public RegisteredStrategy ResolveActive() => ResolveByName(_inner.ResolveActive().Strategy.StrategyName);

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName) =>
        _all.FirstOrDefault(registered => string.Equals(registered.Strategy.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Không tìm thấy strategy '{strategyName}' trong registry test.");

    internal void RecordInvocation() => Interlocked.Increment(ref _invocationCount);
}
