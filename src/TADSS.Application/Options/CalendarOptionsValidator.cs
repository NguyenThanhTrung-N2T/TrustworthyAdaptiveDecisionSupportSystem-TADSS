using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;

namespace TADSS.Application.Options;

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
