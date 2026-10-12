using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Kiểm tra <see cref="ShadowOptions"/> khi khởi động (fail-fast): bật shadow thì <c>Strategy</c> không rỗng; <c>SampleRate</c> trong [0, 1];
/// <c>QueueCapacity</c> trong 1..10000. Tên strategy có trong registry hay không do <c>ShadowDispatcher</c> kiểm (validator không thấy registry).
/// </summary>
public sealed class ShadowOptionsValidator : IValidateOptions<ShadowOptions>
{
    /// <summary>Sức chứa hàng đợi tối đa cho phép.</summary>
    public const int MaxQueueCapacity = 10_000;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ShadowOptions options)
    {
        var failures = new List<string>();
        var section = ShadowOptions.SectionName;

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Strategy))
        {
            failures.Add($"{section}:{nameof(options.Strategy)} không được rỗng khi {section}:{nameof(options.Enabled)} = true.");
        }

        if (!(options.SampleRate >= 0 && options.SampleRate <= 1))
        {
            failures.Add($"{section}:{nameof(options.SampleRate)} phải trong khoảng [0, 1] (hiện là {options.SampleRate}).");
        }

        if (options.QueueCapacity < 1 || options.QueueCapacity > MaxQueueCapacity)
        {
            failures.Add($"{section}:{nameof(options.QueueCapacity)} phải trong khoảng 1..{MaxQueueCapacity} (hiện là {options.QueueCapacity}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
