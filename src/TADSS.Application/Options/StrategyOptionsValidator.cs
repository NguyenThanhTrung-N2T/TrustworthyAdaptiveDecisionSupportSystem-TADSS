using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Kiểm tra <see cref="StrategyOptions"/> khi khởi động (fail-fast): tên strategy đang kích hoạt không được rỗng.
/// Việc tên có khớp một strategy đã đăng ký hay không do <c>StrategyRegistry</c> kiểm tra khi dựng.
/// </summary>
public sealed class StrategyOptionsValidator : IValidateOptions<StrategyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, StrategyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ActiveStrategyName))
        {
            return ValidateOptionsResult.Fail($"{StrategyOptions.SectionName}:{nameof(options.ActiveStrategyName)} không được rỗng.");
        }

        return ValidateOptionsResult.Success;
    }
}
