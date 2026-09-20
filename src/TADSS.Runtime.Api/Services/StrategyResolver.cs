using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Interfaces;
using TADSS.Runtime.Api.Configuration;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Triển khai bộ phân giải chiến lược thẩm định (Strategy Resolver) chuẩn DI và O(1) Lookup.
/// <para>
/// Thực hiện kiểm tra trùng lặp (duplicate check) và xác thực cấu hình chiến lược kích hoạt (startup validation)
/// ngay tại thời điểm khởi tạo nhằm đảm bảo triết lý Fail-fast trong kiến trúc runtime.
/// </para>
/// </summary>
public class StrategyResolver : IStrategyResolver
{
    private readonly Dictionary<string, IReservationDecisionStrategy> _registry;
    private readonly string _activeStrategyName;
    private readonly ILogger<StrategyResolver> _logger;

    /// <summary>
    /// Khởi tạo StrategyResolver với danh sách các chiến lược và cấu hình triển khai.
    /// </summary>
    /// <param name="strategies">Tập hợp các chiến lược được inject từ DI container.</param>
    /// <param name="options">Cấu hình triển khai chỉ định chiến lược hoạt động.</param>
    /// <param name="logger">Logger phục vụ ghi nhận nhật ký hệ thống.</param>
    /// <exception cref="ArgumentNullException">Ném ra khi options hoặc logger bị null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Ném ra khi phát hiện trùng lặp StrategyName hoặc cấu hình ActiveStrategyName không khớp với bất kỳ chiến lược nào đã đăng ký.
    /// </exception>
    public StrategyResolver(
        IEnumerable<IReservationDecisionStrategy> strategies,
        IOptions<StrategyOptions> options,
        ILogger<StrategyResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(strategies);
        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _registry = new Dictionary<string, IReservationDecisionStrategy>(StringComparer.OrdinalIgnoreCase);

        foreach (var strategy in strategies)
        {
            if (_registry.ContainsKey(strategy.StrategyName))
            {
                throw new InvalidOperationException(
                    $"Duplicate StrategyName '{strategy.StrategyName}' detected. " +
                    "Strategy names are unique identities — duplicate registration is prohibited.");
            }

            _registry[strategy.StrategyName] = strategy;
        }

        if (string.IsNullOrWhiteSpace(opts.ActiveStrategyName) || !_registry.ContainsKey(opts.ActiveStrategyName))
        {
            var available = _registry.Count > 0
                ? string.Join(", ", _registry.Keys)
                : "None";

            throw new InvalidOperationException(
                $"Configured ActiveStrategyName '{opts.ActiveStrategyName}' not found in registry. " +
                $"Available registered strategies: [{available}]. Fix configuration section 'Tadss:Strategy:ActiveStrategyName'.");
        }

        _activeStrategyName = opts.ActiveStrategyName;
        _logger.LogInformation("StrategyResolver initialized with active strategy '{ActiveStrategyName}'. Total registered: {Count}",
            _activeStrategyName, _registry.Count);
    }

    /// <inheritdoc />
    public IReservationDecisionStrategy ResolveActive() => _registry[_activeStrategyName];

    /// <inheritdoc />
    public IReservationDecisionStrategy ResolveByName(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        if (_registry.TryGetValue(strategyName, out var strategy))
        {
            return strategy;
        }

        throw new KeyNotFoundException($"Strategy with name '{strategyName}' was not found in the registry.");
    }

    /// <inheritdoc />
    public IReadOnlyList<StrategyRegistration> ListRegistered() =>
        _registry.Values.Select(s => new StrategyRegistration(s.StrategyName, s.StrategyVersion)).ToList();
}
