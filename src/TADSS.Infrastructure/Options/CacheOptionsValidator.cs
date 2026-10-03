using Microsoft.Extensions.Options;

namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểm tra <see cref="CacheOptions"/> khi khởi động (fail-fast): <c>Provider</c> phải là <see cref="CacheProviders.Redis"/> hoặc
/// <see cref="CacheProviders.Memory"/> (không phân biệt hoa thường), <c>RedisTimeoutMs</c>, <c>RedisConnectTimeoutMs</c> và <c>TtlHours</c> phải dương.
/// Áp dụng cho section <c>Tadss:Cache</c>.
/// </summary>
public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CacheOptions options)
    {
        var failures = new List<string>();
        var section = CacheOptions.SectionName;

        if (!CacheProviders.IsRedis(options.Provider) && !CacheProviders.IsMemory(options.Provider))
        {
            failures.Add(
                $"{section}:{nameof(options.Provider)} phải là '{CacheProviders.Redis}' hoặc '{CacheProviders.Memory}' (hiện là '{options.Provider}').");
        }

        RequirePositive(failures, section, nameof(options.RedisTimeoutMs), options.RedisTimeoutMs);
        RequirePositive(failures, section, nameof(options.RedisConnectTimeoutMs), options.RedisConnectTimeoutMs);
        RequirePositive(failures, section, nameof(options.TtlHours), options.TtlHours);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string section, string property, int value)
    {
        if (value <= 0)
        {
            failures.Add($"{section}:{property} phải lớn hơn 0 (hiện là {value}).");
        }
    }
}
