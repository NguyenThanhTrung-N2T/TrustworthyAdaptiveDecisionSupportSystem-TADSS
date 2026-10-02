using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;

namespace TADSS.Runtime.Api.Configuration;

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

/// <summary>
/// Kiểm tra <see cref="CalendarOptions"/> khi khởi động (fail-fast): mỗi khung giờ phải đúng định dạng <c>HH:mm</c>
/// và <c>Start &lt; End</c>. Lỗi domain của SDK được chuyển thành lỗi validation chỉ rõ phần tử.
/// </summary>
public sealed class CalendarOptionsValidator : IValidateOptions<CalendarOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CalendarOptions options)
    {
        var failures = new List<string>();

        for (var index = 0; index < options.PeakWindows.Count; index++)
        {
            var location = $"{CalendarOptions.SectionName}:PeakWindows[{index}]";
            var window = options.PeakWindows[index];

            if (window is null)
            {
                failures.Add($"{location} không được null.");
                continue;
            }

            var failuresBefore = failures.Count;
            if (window.Day is null)
            {
                failures.Add($"{location}.Day là bắt buộc (Monday…Sunday).");
            }

            if (!PeakWindowOptions.TryParseTime(window.Start, out _))
            {
                failures.Add($"{location}.Start '{window.Start}' không đúng định dạng {PeakWindowOptions.TimeFormat}.");
            }

            if (!PeakWindowOptions.TryParseTime(window.End, out _))
            {
                failures.Add($"{location}.End '{window.End}' không đúng định dạng {PeakWindowOptions.TimeFormat}.");
            }

            if (failures.Count > failuresBefore)
            {
                continue;
            }

            try
            {
                _ = window.ToPeakWindow();
            }
            catch (DomainInvariantException exception)
            {
                failures.Add($"{location} không hợp lệ: Start phải nhỏ hơn End và Day phải hợp lệ ({exception.Message}).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
