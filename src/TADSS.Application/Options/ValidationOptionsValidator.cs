using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Kiểm tra <see cref="ValidationOptions"/> khi khởi động (fail-fast): <c>0 &lt; MinItemMinutes ≤ MaxItemMinutes</c> (P-01).
/// </summary>
public sealed class ValidationOptionsValidator : IValidateOptions<ValidationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ValidationOptions options)
    {
        if (options.MinItemMinutes <= 0 || options.MinItemMinutes > options.MaxItemMinutes)
        {
            return ValidateOptionsResult.Fail(
                $"{ValidationOptions.SectionName} (P-01) yêu cầu 0 < MinItemMinutes <= MaxItemMinutes " +
                $"(hiện là MinItemMinutes={options.MinItemMinutes}, MaxItemMinutes={options.MaxItemMinutes}).");
        }

        return ValidateOptionsResult.Success;
    }
}
