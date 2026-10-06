using System.Diagnostics.CodeAnalysis;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Strategies;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Registry thay thế chỉ dùng trong host test (C-10): giữ nguyên các strategy của registry <paramref name="inner"/> và thêm một strategy do test cung cấp
/// (dấu vân tay cố định). Dùng khi cần strategy chính là strategy thật nhưng strategy shadow là strategy giả có hành vi điều khiển được.
/// Không có gì ở mã Production.
/// </summary>
public sealed class ExtendedStrategyRegistry : IStrategyRegistry
{
    private const string Fingerprint = "f2f2f2f2f2f2f2f2";
    private readonly IStrategyRegistry _inner;
    private readonly RegisteredStrategy _extra;

    /// <summary>Bọc <paramref name="inner"/> và thêm <paramref name="extra"/>.</summary>
    public ExtendedStrategyRegistry(IStrategyRegistry inner, IReservationDecisionStrategy extra)
    {
        _inner = inner;
        _extra = new RegisteredStrategy(extra, Fingerprint);
        All = [.. inner.All, _extra];
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All { get; }

    /// <inheritdoc />
    public bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy)
    {
        if (string.Equals(_extra.Strategy.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase))
        {
            strategy = _extra;
            return true;
        }

        return _inner.TryResolve(strategyName, out strategy);
    }

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName) =>
        TryResolve(strategyName, out var registered)
            ? registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy '{strategyName}' trong registry test.");
}
