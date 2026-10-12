using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace TADSS.Application.Strategies;

/// <summary>
/// Triển khai <see cref="IStrategyRegistry"/>: tra O(1) theo tên (không phân biệt hoa thường) và kiểm tra ngay khi khởi
/// tạo (fail-fast) việc trùng tên strategy. Strategy mặc định và strategy theo rule do <c>StrategyRouter</c> chọn (<c>Tadss:Routing</c>).
/// </summary>
public sealed class StrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<string, RegisteredStrategy> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegisteredStrategy> _ordered = [];

    /// <summary>
    /// Dựng registry từ các strategy đã đăng ký.
    /// </summary>
    /// <param name="strategies">Các strategy (kèm fingerprint) lấy từ DI.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="InvalidOperationException">Trùng <c>StrategyName</c>.</exception>
    public StrategyRegistry(IEnumerable<RegisteredStrategy> strategies, ILogger<StrategyRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(logger);

        foreach (var registered in strategies)
        {
            var name = registered.Strategy.StrategyName;
            if (!_byName.TryAdd(name, registered))
            {
                throw new InvalidOperationException(
                    $"Trùng StrategyName '{name}' trong registry strategy v2. Tên strategy là định danh duy nhất, không được đăng ký trùng.");
            }

            _ordered.Add(registered);
        }

        All = _ordered.AsReadOnly();
        logger.LogInformation(
            "Registry strategy v2 khởi tạo với {Count} strategy: [{StrategyNames}].",
            _ordered.Count,
            string.Join(", ", _ordered.Select(registered => $"{registered.Strategy.StrategyName} ({registered.OptionsFingerprint})")));
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All { get; }

    /// <inheritdoc />
    public bool TryResolve(string strategyName, [NotNullWhen(true)] out RegisteredStrategy? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
        {
            strategy = null;
            return false;
        }

        return _byName.TryGetValue(strategyName, out strategy);
    }

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        return _byName.TryGetValue(strategyName, out var registered)
            ? registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy v2 tên '{strategyName}' trong registry.");
    }
}
