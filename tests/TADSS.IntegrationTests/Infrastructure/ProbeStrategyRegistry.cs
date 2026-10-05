using System.Diagnostics.CodeAnalysis;
using Shared.DecisionContractSDK.Decisions;
using TADSS.Application.Strategies;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Double của <see cref="IStrategyRegistry"/> chỉ dùng trong host test (C-10): bọc registry thật, trả mọi strategy dưới dạng
/// <see cref="ProbeStrategy"/> (cùng fingerprint tham số) để test đếm số lần strategy chạy và ghi đè chính sách cache. Router thật của host định tuyến
/// qua registry này nên mọi strategy được chọn (mặc định hay theo rule) đều là probe.
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
    public bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy)
    {
        strategy = _all.FirstOrDefault(registered => string.Equals(registered.Strategy.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase));
        return strategy is not null;
    }

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName) =>
        TryResolve(strategyName, out var registered)
            ? registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy '{strategyName}' trong registry test.");

    internal void RecordInvocation() => Interlocked.Increment(ref _invocationCount);
}
