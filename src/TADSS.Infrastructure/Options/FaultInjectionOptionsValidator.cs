using Microsoft.Extensions.Options;

namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểm tra <see cref="FaultInjectionOptions"/> khi khởi động (fail-fast, ở mọi môi trường): <c>DelayMs</c> trong 1..60000 và
/// <c>TargetStrategy</c> nếu có thì không chỉ gồm khoảng trắng. Tên strategy có trong registry hay không không kiểm ở đây
/// (validator không thấy registry): tên lạ chỉ làm decorator không áp cho strategy nào, <c>StartupTasks</c> ghi Warning.
/// Áp dụng cho section <c>Tadss:Testing:FaultInjection</c>.
/// </summary>
public sealed class FaultInjectionOptionsValidator : IValidateOptions<FaultInjectionOptions>
{
    /// <summary>Giới hạn dưới của <c>DelayMs</c> (ms).</summary>
    public const int MinDelayMs = 1;

    /// <summary>Giới hạn trên của <c>DelayMs</c> (ms).</summary>
    public const int MaxDelayMs = 60_000;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FaultInjectionOptions options)
    {
        var failures = new List<string>();
        var section = FaultInjectionOptions.SectionName;

        if (options.DelayMs is < MinDelayMs or > MaxDelayMs)
        {
            failures.Add($"{section}:{nameof(options.DelayMs)} phải trong khoảng {MinDelayMs}..{MaxDelayMs} ms (hiện là {options.DelayMs}).");
        }

        if (options.TargetStrategy is { Length: > 0 } target && string.IsNullOrWhiteSpace(target))
        {
            failures.Add($"{section}:{nameof(options.TargetStrategy)} không được chỉ gồm khoảng trắng (bỏ trống để áp cho mọi strategy).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
