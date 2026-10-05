using System.Diagnostics.CodeAnalysis;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Strategies;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Registry thay thế chỉ dùng trong host test (C-10): chỉ có một strategy v2 do test cung cấp, bọc thành <see cref="RegisteredStrategy"/>
/// với dấu vân tay cố định (16 ký tự hex thường). Host test đặt <c>Tadss:Routing:DefaultStrategy</c> bằng tên strategy đó để router thật chọn nó
/// (xem <see cref="FaultInjectionTestBase"/>). Không có gì ở mã Production.
/// </summary>
public sealed class ScriptedStrategyRegistry : IStrategyRegistry
{
    private const string Fingerprint = "f1f1f1f1f1f1f1f1";
    private readonly RegisteredStrategy _registered;

    /// <summary>Bọc <paramref name="strategy"/> làm strategy duy nhất của registry.</summary>
    public ScriptedStrategyRegistry(IReservationDecisionStrategy strategy)
    {
        _registered = new RegisteredStrategy(strategy, Fingerprint);
        All = [_registered];
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All { get; }

    /// <inheritdoc />
    public bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy)
    {
        strategy = string.Equals(_registered.Strategy.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase) ? _registered : null;
        return strategy is not null;
    }

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName) =>
        TryResolve(strategyName, out var registered)
            ? registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy '{strategyName}' trong registry test.");
}
