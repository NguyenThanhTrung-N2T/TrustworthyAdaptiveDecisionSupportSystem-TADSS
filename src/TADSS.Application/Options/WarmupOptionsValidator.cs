using Microsoft.Extensions.Options;

namespace TADSS.Application.Options;

/// <summary>
/// Validator fail-fast cho <see cref="WarmupOptions"/>: số kết nối, số vòng và ngân sách thời gian phải trong khoảng hợp lý.
/// </summary>
public sealed class WarmupOptionsValidator : IValidateOptions<WarmupOptions>
{
    /// <summary>Số kết nối mở sẵn tối đa (bằng kích thước pool mặc định của Npgsql).</summary>
    public const int MaxDatabaseConnections = 100;

    /// <summary>Số vòng chạy thử tối đa.</summary>
    public const int MaxIterations = 100;

    /// <summary>Ngân sách thời gian tối thiểu (mili giây).</summary>
    public const int MinTimeoutMs = 100;

    /// <summary>Ngân sách thời gian tối đa (mili giây): làm nóng không được giữ khởi động quá 2 phút.</summary>
    public const int MaxTimeoutMs = 120_000;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WarmupOptions options)
    {
        var failures = new List<string>();
        var section = WarmupOptions.SectionName;

        if (options.DatabaseConnections < 0 || options.DatabaseConnections > MaxDatabaseConnections)
        {
            failures.Add($"{section}:{nameof(options.DatabaseConnections)} phải trong khoảng 0..{MaxDatabaseConnections} (hiện là {options.DatabaseConnections}).");
        }

        if (options.Iterations < 1 || options.Iterations > MaxIterations)
        {
            failures.Add($"{section}:{nameof(options.Iterations)} phải trong khoảng 1..{MaxIterations} (hiện là {options.Iterations}).");
        }

        if (options.TimeoutMs < MinTimeoutMs || options.TimeoutMs > MaxTimeoutMs)
        {
            failures.Add($"{section}:{nameof(options.TimeoutMs)} phải trong khoảng {MinTimeoutMs}..{MaxTimeoutMs} (hiện là {options.TimeoutMs}).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
