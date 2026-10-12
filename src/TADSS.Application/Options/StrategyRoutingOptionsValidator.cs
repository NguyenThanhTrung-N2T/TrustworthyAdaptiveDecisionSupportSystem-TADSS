using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Kiểm tra <see cref="StrategyRoutingOptions"/> khi khởi động (fail-fast) và mỗi lần nạp lại (A-01, A-02): <c>DefaultStrategy</c> không rỗng; mỗi rule
/// có <c>Name</c> không rỗng, tối đa <see cref="MaxRuleNameLength"/> ký tự, duy nhất (không phân biệt hoa thường); <c>Strategy</c> không rỗng; <c>When</c>
/// có ít nhất một điều kiện và giá trị enum đã định nghĩa. Thông báo nêu đường dẫn khóa cấu hình, không kèm giá trị cấu hình khác.
/// Tên strategy có trong registry hay không do <c>StrategyRouter</c> kiểm (validator không thấy registry).
/// </summary>
public sealed class StrategyRoutingOptionsValidator : IValidateOptions<StrategyRoutingOptions>
{
    /// <summary>Độ dài tối đa của tên rule.</summary>
    public const int MaxRuleNameLength = 64;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, StrategyRoutingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.DefaultStrategy))
        {
            failures.Add($"{StrategyRoutingOptions.SectionName}:DefaultStrategy không được rỗng (strategy dùng khi không rule nào khớp).");
        }

        var rules = options.Rules ?? [];
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rules.Count; index++)
        {
            var location = $"{StrategyRoutingOptions.SectionName}:Rules:{index}";
            var rule = rules[index];
            if (rule is null)
            {
                failures.Add($"{location} không được null.");
                continue;
            }

            ValidateName(rule.Name, location, index, firstIndexByName, failures);

            if (string.IsNullOrWhiteSpace(rule.Strategy))
            {
                failures.Add($"{location}:Strategy là bắt buộc (tên strategy khi rule khớp).");
            }

            ValidateCondition(rule.When, $"{location}:When", failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateName(string? ruleName, string location, int index, Dictionary<string, int> firstIndexByName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            failures.Add($"{location}:Name là bắt buộc.");
            return;
        }

        if (ruleName.Length > MaxRuleNameLength)
        {
            failures.Add($"{location}:Name dài {ruleName.Length} ký tự, tối đa {MaxRuleNameLength}.");
            return;
        }

        if (!firstIndexByName.TryAdd(ruleName, index))
        {
            failures.Add(
                $"{location}:Name '{ruleName}' trùng với {StrategyRoutingOptions.SectionName}:Rules:{firstIndexByName[ruleName]}:Name (tên rule không phân biệt hoa thường).");
        }
    }

    private static void ValidateCondition(RoutingConditionOptions? when, string location, List<string> failures)
    {
        if (when is null || !when.HasAnyCondition())
        {
            failures.Add(
                $"{location} phải có ít nhất một điều kiện (PeakHour, BranchIds, Channels, Tiers, IsGuest): rule không điều kiện khớp mọi request và che các rule sau.");
            return;
        }

        AddUndefinedValues(when.Channels, $"{location}:Channels", failures);
        AddUndefinedValues(when.Tiers, $"{location}:Tiers", failures);
    }

    private static void AddUndefinedValues<TEnum>(List<TEnum>? values, string location, List<string> failures)
        where TEnum : struct, Enum
    {
        if (values is null)
        {
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (!Enum.IsDefined(values[index]))
            {
                failures.Add($"{location}:{index} không phải giá trị hợp lệ ({string.Join(", ", Enum.GetNames<TEnum>())}).");
            }
        }
    }
}
