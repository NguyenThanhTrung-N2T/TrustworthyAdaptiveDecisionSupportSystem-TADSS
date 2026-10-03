using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Kiểm tra <see cref="ApprovalOptions"/> khi khởi động (fail-fast).
/// </summary>
public sealed class ApprovalOptionsValidator : IValidateOptions<ApprovalOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ApprovalOptions options)
    {
        var failures = new List<string>();

        if (options.WindowMinutes <= 0)
        {
            failures.Add($"{ApprovalOptions.SectionName}:WindowMinutes phải lớn hơn 0 (hiện là {options.WindowMinutes}).");
        }

        if (options.MinLeadMinutes < 0)
        {
            failures.Add($"{ApprovalOptions.SectionName}:MinLeadMinutes không được âm (hiện là {options.MinLeadMinutes}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
