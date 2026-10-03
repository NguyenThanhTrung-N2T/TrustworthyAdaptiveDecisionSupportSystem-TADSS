using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;

namespace TADSS.Application.Strategies;

/// <summary>
/// Triển khai <see cref="IStrategyRegistry"/>: tra O(1) theo tên (không phân biệt hoa thường) và kiểm tra ngay khi khởi
/// tạo (fail-fast) việc trùng tên strategy và tên strategy kích hoạt. Đọc section
/// <c>Tadss:Strategy:ActiveStrategyName</c>.
/// </summary>
public sealed class StrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<string, RegisteredStrategy> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RegisteredStrategy> _ordered = [];
    private readonly RegisteredStrategy _active;

    /// <summary>
    /// Dựng registry từ các strategy đã đăng ký.
    /// </summary>
    /// <param name="strategies">Các strategy v2 (kèm fingerprint) lấy từ DI.</param>
    /// <param name="options">Cấu hình chỉ định strategy kích hoạt.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="InvalidOperationException">Trùng <c>StrategyName</c>, hoặc strategy kích hoạt rỗng/không có trong registry.</exception>
    public StrategyRegistry(
        IEnumerable<RegisteredStrategy> strategies,
        IOptions<StrategyOptions> options,
        ILogger<StrategyRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var activeName = options.Value.ActiveStrategyName;

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

        if (string.IsNullOrWhiteSpace(activeName) || !_byName.TryGetValue(activeName, out var active))
        {
            var available = _byName.Count > 0 ? string.Join(", ", _byName.Keys) : "không có";
            throw new InvalidOperationException(
                $"ActiveStrategyName '{activeName}' không có trong registry strategy v2. " +
                $"Các strategy hợp lệ: [{available}]. Sửa cấu hình '{StrategyOptions.SectionName}:{nameof(StrategyOptions.ActiveStrategyName)}'.");
        }

        _active = active;
        logger.LogInformation(
            "Registry strategy v2 khởi tạo với strategy kích hoạt '{ActiveStrategyName}' (fingerprint {Fingerprint}); tổng số đăng ký: {Count}",
            _active.Strategy.StrategyName, _active.OptionsFingerprint, _byName.Count);
    }

    /// <inheritdoc />
    public IReadOnlyList<RegisteredStrategy> All { get; }

    /// <inheritdoc />
    public RegisteredStrategy ResolveActive() => _active;

    /// <inheritdoc />
    public RegisteredStrategy ResolveByName(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

        return _byName.TryGetValue(strategyName, out var registered)
            ? registered
            : throw new KeyNotFoundException($"Không tìm thấy strategy v2 tên '{strategyName}' trong registry.");
    }
}
