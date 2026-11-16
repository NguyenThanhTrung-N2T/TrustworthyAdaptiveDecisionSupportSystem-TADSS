using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Shared.DecisionContractSDK.Enums;
using TADSS.Application.Options;

namespace TADSS.Infrastructure.Configuration;

/// <summary>
/// Bind <c>Tadss:Routing</c> từ <see cref="IConfiguration"/> và phát hiện lỗi mà binder của .NET <b>nuốt</b>: binder bind từng phần tử danh sách trong
/// một khối bắt mọi exception rồi bỏ phần tử đó, nên một giá trị sai kiểu bên trong rule (ví dụ <c>PeakHour: "maybe"</c>) làm <b>mất cả rule</b>, còn
/// phần tử sai kiểu của <c>BranchIds</c>/<c>Channels</c>/<c>Tiers</c> (ví dụ <c>Channels: ["PHONE"]</c>) hoặc cả danh sách khai báo bằng một giá trị đơn (<c>Channels: "ONLINE"</c>) bị bỏ khiến rule rộng hơn ý định. Lớp này bind
/// riêng từng rule để lấy lỗi của binder và kiểm từng phần tử danh sách bằng đúng bộ chuyển kiểu mà binder dùng; lỗi nêu đường dẫn khóa, không kèm giá trị.
/// Khóa không được hỗ trợ (gõ sai tên điều kiện như <c>When:PeakHours</c>, hoặc điều kiện runtime chưa có) cũng là lỗi: binder bỏ qua khóa lạ âm thầm nên
/// rule sẽ rộng hơn ý định; có lỗi thì router từ chối cả bảng (khi khởi động: không chạy; khi nạp lại: giữ bảng cũ).
/// </summary>
internal static class RoutingConfigurationBinder
{
    /// <summary>Bind bản mới từ section <see cref="StrategyRoutingOptions.SectionName"/>; lỗi binder đã nuốt được thêm vào <paramref name="failures"/>.</summary>
    public static StrategyRoutingOptions Bind(IConfiguration configuration, ICollection<string> failures)
    {
        var section = configuration.GetSection(StrategyRoutingOptions.SectionName);
        var options = new StrategyRoutingOptions();
        section.Bind(options);
        foreach (var failure in FindSwallowedBindingFailures(section))
        {
            failures.Add(failure);
        }

        return options;
    }

    /// <summary>
    /// Lỗi binder đã nuốt trong section định tuyến: rule không bind được (giá trị đơn sai kiểu hoặc rule rỗng) và phần tử
    /// <c>BranchIds</c>/<c>Channels</c>/<c>Tiers</c> rỗng hoặc không chuyển được sang kiểu đích.
    /// </summary>
    public static IEnumerable<string> FindSwallowedBindingFailures(IConfigurationSection routingSection)
    {
        ArgumentNullException.ThrowIfNull(routingSection);

        var failures = new List<string>();
        CheckKnownKeys(routingSection, RoutingKeys, failures);
        foreach (var rule in routingSection.GetSection(nameof(StrategyRoutingOptions.Rules)).GetChildren())
        {
            CheckRule(rule, failures);
            CheckKnownKeys(rule, RuleKeys, failures);
            var when = rule.GetSection(nameof(RoutingRuleOptions.When));
            CheckKnownKeys(when, ConditionKeys, failures);
            CheckElements<Guid>(when.GetSection(nameof(RoutingConditionOptions.BranchIds)), failures);
            CheckElements<Channel>(when.GetSection(nameof(RoutingConditionOptions.Channels)), failures);
            CheckElements<CustomerTier>(when.GetSection(nameof(RoutingConditionOptions.Tiers)), failures);
        }

        return failures;
    }

    // Khóa được hỗ trợ = thuộc tính ghi được của POCO tương ứng (không phân biệt hoa thường, như binder)
    private static readonly HashSet<string> RoutingKeys = WritablePropertyNames<StrategyRoutingOptions>();
    private static readonly HashSet<string> RuleKeys = WritablePropertyNames<RoutingRuleOptions>();
    private static readonly HashSet<string> ConditionKeys = WritablePropertyNames<RoutingConditionOptions>();

    private static HashSet<string> WritablePropertyNames<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Khóa lạ (gõ sai tên điều kiện, điều kiện chưa hỗ trợ) bị binder bỏ qua âm thầm nên rule rộng hơn ý định: báo lỗi để cả bảng bị từ chối
    private static void CheckKnownKeys(IConfigurationSection section, HashSet<string> knownKeys, List<string> failures)
    {
        foreach (var child in section.GetChildren())
        {
            if (!knownKeys.Contains(child.Key))
            {
                failures.Add($"{child.Path} không phải khóa được hỗ trợ (hợp lệ: {string.Join(", ", knownKeys.Order(StringComparer.Ordinal))}).");
            }
        }
    }

    private static void CheckRule(IConfigurationSection rule, List<string> failures)
    {
        try
        {
            if (rule.Get<RoutingRuleOptions>() is null)
            {
                failures.Add($"{rule.Path} rỗng, không phải một rule.");
            }
        }
        catch (InvalidOperationException exception)
        {
            // Thông báo của binder nêu đường dẫn khóa và kiểu đích (không kèm giá trị)
            failures.Add($"{rule.Path} không bind được: {exception.Message}");
        }
    }

    private static void CheckElements<T>(IConfigurationSection list, List<string> failures)
    {
        // Danh sách khai báo bằng một giá trị đơn (Channels: "ONLINE") thay vì mảng: binder bỏ qua âm thầm, danh sách rỗng và rule rộng hơn ý định
        if (list.Value is not null)
        {
            failures.Add($"{list.Path} phải là danh sách (mảng), không phải giá trị đơn.");
            return;
        }

        var converter = TypeDescriptor.GetConverter(typeof(T));
        foreach (var element in list.GetChildren())
        {
            if (!CanConvert(converter, element.Value))
            {
                failures.Add($"{element.Path} không chuyển được sang {typeof(T).Name}.");
            }
        }
    }

    private static bool CanConvert(TypeConverter converter, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return converter.ConvertFromInvariantString(value) is not null;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
