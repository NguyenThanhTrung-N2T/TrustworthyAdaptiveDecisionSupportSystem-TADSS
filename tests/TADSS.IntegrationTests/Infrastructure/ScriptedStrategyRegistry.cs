using Shared.DecisionContractSDK.Interfaces;
using TADSS.Application.Strategies;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Registry thay thế chỉ dùng trong host test (C-10): chỉ có một strategy v2 do test cung cấp, bọc thành <see cref="RegisteredStrategy"/>
/// với dấu vân tay cố định (16 ký tự hex thường). Không có gì ở mã Production.
/// </summary>
public sealed class ScriptedStrategyRegistry : IStrategyRegistry
{
    private const string Fingerprint = "f1f1f1f1f1f1f1f1";
    private readonly RegisteredStrategy _registered;

    /// <summary>Bọc <paramref name="strategy"/> làm strategy duy nhất (và là strategy đang kích hoạt).</summary>
    public ScriptedStrategyRegistry(IReservationDecisionStrategy strategy)
    {
        _registered = new RegisteredStrategy(strategy, Fingerprint);
        All = [_registered];
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All { get; }

    /// <inheritdoc />
    public RegisteredStrategy ResolveActive() => _registered;

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName) =>
        string.Equals(_registered.Strategy.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase)
            ? _registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy '{strategyName}' trong registry test.");
}
