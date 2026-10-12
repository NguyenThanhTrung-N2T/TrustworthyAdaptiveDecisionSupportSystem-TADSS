using Microsoft.Extensions.Options;

namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểm tra <see cref="CacheOptions"/> khi khởi động (fail-fast): <c>Provider</c> phải là <see cref="CacheProviders.Redis"/> hoặc
/// <see cref="CacheProviders.Memory"/> (không phân biệt hoa thường), <c>RedisTimeoutMs</c> và <c>RedisConnectTimeoutMs</c> phải dương,
/// <c>InstanceName</c> không rỗng, tối đa 32 ký tự và không chứa khoảng trắng, tham số breaker Redis trong khoảng hợp lệ
/// (<c>RedisBreakerFailureRatio</c> trong (0, 1], <c>RedisBreakerSamplingSeconds</c> ≥ 1, <c>RedisBreakerMinimumThroughput</c> ≥ 2,
/// <c>RedisBreakerBreakDurationSeconds</c> ≥ 1). Áp dụng cho section <c>Tadss:Cache</c>.
/// </summary>
public sealed class CacheOptionsValidator : IValidateOptions<CacheOptions>
{
    private const int MaxInstanceNameLength = 32;

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

        RequireAtLeast(failures, section, nameof(options.RedisTimeoutMs), options.RedisTimeoutMs, 1);
        RequireAtLeast(failures, section, nameof(options.RedisConnectTimeoutMs), options.RedisConnectTimeoutMs, 1);
        RequireAtLeast(failures, section, nameof(options.RedisBreakerSamplingSeconds), options.RedisBreakerSamplingSeconds, 1);
        RequireAtLeast(failures, section, nameof(options.RedisBreakerMinimumThroughput), options.RedisBreakerMinimumThroughput, 2);
        RequireAtLeast(failures, section, nameof(options.RedisBreakerBreakDurationSeconds), options.RedisBreakerBreakDurationSeconds, 1);

        if (!(options.RedisBreakerFailureRatio > 0 && options.RedisBreakerFailureRatio <= 1))
        {
            failures.Add($"{section}:{nameof(options.RedisBreakerFailureRatio)} phải trong khoảng (0, 1] (hiện là {options.RedisBreakerFailureRatio}).");
        }

        if (string.IsNullOrWhiteSpace(options.InstanceName)
            || options.InstanceName.Length > MaxInstanceNameLength
            || options.InstanceName.Any(char.IsWhiteSpace))
        {
            failures.Add($"{section}:{nameof(options.InstanceName)} phải không rỗng, tối đa {MaxInstanceNameLength} ký tự và không chứa khoảng trắng.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireAtLeast(List<string> failures, string section, string property, int value, int minimum)
    {
        if (value < minimum)
        {
            failures.Add(minimum == 1
                ? $"{section}:{property} phải lớn hơn 0 (hiện là {value})."
                : $"{section}:{property} phải từ {minimum} trở lên (hiện là {value}).");
        }
    }
}
