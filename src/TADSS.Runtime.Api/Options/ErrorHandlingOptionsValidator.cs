using Microsoft.Extensions.Options;

namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Kiểm tra <see cref="ErrorHandlingOptions"/> khi khởi động (fail-fast): <c>1 ≤ RetryAfterSeconds ≤ 3600</c>.
/// Giá trị lớn hơn 1 giờ gần như chắc chắn là cấu hình nhầm đơn vị (client của BE chỉ retry trong vòng một lần đặt sân).
/// </summary>
public sealed class ErrorHandlingOptionsValidator : IValidateOptions<ErrorHandlingOptions>
{
    /// <summary>Giá trị lớn nhất cho phép của <see cref="ErrorHandlingOptions.RetryAfterSeconds"/> (giây).</summary>
    public const int MaxRetryAfterSeconds = 3600;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ErrorHandlingOptions options)
    {
        if (options.RetryAfterSeconds < 1 || options.RetryAfterSeconds > MaxRetryAfterSeconds)
        {
            return ValidateOptionsResult.Fail(
                $"{ErrorHandlingOptions.SectionName}:{nameof(options.RetryAfterSeconds)} phải nằm trong khoảng 1..{MaxRetryAfterSeconds} giây (hiện là {options.RetryAfterSeconds}).");
        }

        return ValidateOptionsResult.Success;
    }
}
