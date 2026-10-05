using System.Text;
using Shared.DecisionContractSDK.Domain;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Options;
using TADSS.Application.Strategies;

namespace TADSS.Application.Routing;

/// <summary>
/// Bảng định tuyến đã biên dịch, bất biến (A-01, A-02): strategy của mặc định và của từng rule đã phân giải sẵn từ registry, điều kiện dạng
/// <see cref="HashSet{T}"/>, kết quả <see cref="StrategyRoute"/> dựng sẵn. <see cref="Route"/> không I/O, không cấp phát kết quả mới, an toàn đa luồng
/// (chỉ đọc). <see cref="Signature"/> là mô tả chuẩn hóa để so sánh hai bảng (nạp lại không đổi gì thì chỉ log Debug).
/// </summary>
internal sealed class RoutingTable
{
    private readonly CompiledRule[] _rules;

    private RoutingTable(StrategyRoute defaultRoute, CompiledRule[] rules, string signature)
    {
        DefaultRoute = defaultRoute;
        _rules = rules;
        Signature = signature;
    }

    /// <summary>Kết quả khi không rule nào khớp.</summary>
    public StrategyRoute DefaultRoute { get; }

    /// <summary>Số rule.</summary>
    public int RuleCount => _rules.Length;

    /// <summary>Tên các rule theo thứ tự ưu tiên (chỉ dùng để log).</summary>
    public string RuleNames => string.Join(", ", _rules.Select(rule => rule.Route.RuleName));

    /// <summary>Mô tả chuẩn hóa của bảng (tên strategy chuẩn, tập điều kiện đã sắp xếp); hai bảng tương đương có cùng chữ ký.</summary>
    public string Signature { get; }

    /// <summary>
    /// Biên dịch <paramref name="options"/> (đã qua validator) thành bảng. Tên strategy phân giải qua <paramref name="registry"/>, không phân biệt hoa thường.
    /// </summary>
    /// <exception cref="InvalidOperationException">Có tên strategy không có trong registry; thông báo liệt kê từng tên lạ kèm khóa cấu hình và các strategy hợp lệ.</exception>
    public static RoutingTable Compile(StrategyRoutingOptions options, IStrategyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);

        var unknown = new List<string>();
        var defaultStrategy = Resolve(registry, options.DefaultStrategy, $"{StrategyRoutingOptions.SectionName}:DefaultStrategy", unknown);

        var sourceRules = options.Rules ?? [];
        var rules = new CompiledRule[sourceRules.Count];
        for (var index = 0; index < sourceRules.Count; index++)
        {
            var rule = sourceRules[index];
            var strategy = Resolve(registry, rule.Strategy, $"{StrategyRoutingOptions.SectionName}:Rules:{index}:Strategy", unknown);
            if (strategy is not null)
            {
                rules[index] = new CompiledRule(new StrategyRoute(strategy, rule.Name), rule.When ?? new RoutingConditionOptions());
            }
        }

        if (unknown.Count > 0 || defaultStrategy is null)
        {
            var available = registry.All.Count > 0 ? string.Join(", ", registry.All.Select(registered => registered.Strategy.StrategyName)) : "không có";
            throw new InvalidOperationException(
                $"Cấu hình {StrategyRoutingOptions.SectionName} tham chiếu strategy không có trong registry: {string.Join("; ", unknown)}. " +
                $"Các strategy hợp lệ: [{available}].");
        }

        var defaultRoute = new StrategyRoute(defaultStrategy, null);
        return new RoutingTable(defaultRoute, rules, BuildSignature(defaultRoute, rules));
    }

    /// <summary>Rule đầu tiên khớp <paramref name="reservation"/>, không khớp thì <see cref="DefaultRoute"/>.</summary>
    public StrategyRoute Route(Reservation reservation, BranchCalendar calendar)
    {
        // Giờ cao điểm chỉ tính khi có rule cần và tối đa một lần cho mỗi request
        bool? isPeak = null;
        foreach (var rule in _rules)
        {
            if (rule.Matches(reservation, calendar, ref isPeak))
            {
                return rule.Route;
            }
        }

        return DefaultRoute;
    }

    private static RegisteredStrategy? Resolve(IStrategyRegistry registry, string? strategyName, string key, List<string> unknown)
    {
        if (strategyName is not null && registry.TryResolve(strategyName, out var registered))
        {
            return registered;
        }

        unknown.Add($"'{strategyName}' ({key})");
        return null;
    }

    private static string BuildSignature(StrategyRoute defaultRoute, CompiledRule[] rules)
    {
        var builder = new StringBuilder("default=").Append(defaultRoute.Strategy.Strategy.StrategyName);
        foreach (var rule in rules)
        {
            builder.Append("|rule=").Append(rule.Route.RuleName)
                .Append('>').Append(rule.Route.Strategy.Strategy.StrategyName)
                .Append(";peak=").Append(rule.PeakHour)
                .Append(";branches=").AppendJoin(',', Sorted(rule.BranchIds))
                .Append(";channels=").AppendJoin(',', Sorted(rule.Channels))
                .Append(";tiers=").AppendJoin(',', Sorted(rule.Tiers))
                .Append(";guest=").Append(rule.IsGuest);
        }

        return builder.ToString();
    }

    private static IEnumerable<T> Sorted<T>(HashSet<T>? values) => values is null ? [] : values.Order();

    /// <summary>Một rule đã biên dịch: <c>null</c> ở mỗi điều kiện nghĩa là không ràng buộc.</summary>
    private sealed class CompiledRule
    {
        public CompiledRule(StrategyRoute route, RoutingConditionOptions when)
        {
            Route = route;
            PeakHour = when.PeakHour;
            IsGuest = when.IsGuest;
            BranchIds = ToSet(when.BranchIds);
            Channels = ToSet(when.Channels);
            Tiers = ToSet(when.Tiers);
        }

        public StrategyRoute Route { get; }

        public bool? PeakHour { get; }

        public bool? IsGuest { get; }

        public HashSet<Guid>? BranchIds { get; }

        public HashSet<Channel>? Channels { get; }

        public HashSet<CustomerTier>? Tiers { get; }

        /// <summary>Mọi điều kiện được đặt đều khớp (AND). <paramref name="isPeak"/> là giờ cao điểm đã tính cho request (tính lười, dùng lại giữa các rule).</summary>
        public bool Matches(Reservation reservation, BranchCalendar calendar, ref bool? isPeak)
        {
            if (BranchIds is not null && !BranchIds.Contains(reservation.BranchId))
            {
                return false;
            }

            if (Channels is not null && !Channels.Contains(reservation.Channel))
            {
                return false;
            }

            if (Tiers is not null && !Tiers.Contains(reservation.Customer.Tier))
            {
                return false;
            }

            if (IsGuest is { } guest && reservation.Customer.IsGuest != guest)
            {
                return false;
            }

            if (PeakHour is { } peak)
            {
                isPeak ??= calendar.IsPeak(reservation);
                return isPeak.Value == peak;
            }

            return true;
        }

        private static HashSet<T>? ToSet<T>(List<T>? values) => values is { Count: > 0 } ? [.. values] : null;
    }
}
