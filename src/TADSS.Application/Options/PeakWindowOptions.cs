using System.Globalization;
using Shared.DecisionContractSDK.Domain;

namespace TADSS.Application.Options;

/// <summary>
/// Một khung giờ cao điểm trong cấu hình: ngày trong tuần và giờ bắt đầu/kết thúc theo định dạng <c>HH:mm</c>
/// (giờ địa phương của chi nhánh).
/// </summary>
public class PeakWindowOptions
{
    /// <summary>
    /// Định dạng giờ chấp nhận được.
    /// </summary>
    public const string TimeFormat = "HH:mm";

    /// <summary>
    /// Ngày trong tuần; bắt buộc (thiếu thì <c>CalendarOptionsValidator</c> từ chối thay vì ngầm hiểu là Chủ Nhật).
    /// </summary>
    public DayOfWeek? Day { get; set; }

    /// <summary>
    /// Giờ bắt đầu (tính cả), định dạng <c>HH:mm</c>.
    /// </summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>
    /// Giờ kết thúc (không tính), định dạng <c>HH:mm</c>, sau <see cref="Start"/>.
    /// </summary>
    public string End { get; set; } = string.Empty;

    /// <summary>
    /// Đọc một chuỗi giờ theo <see cref="TimeFormat"/> với invariant culture.
    /// </summary>
    internal static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>
    /// Chuyển thành <see cref="PeakWindow"/> của SDK.
    /// </summary>
    /// <exception cref="InvalidOperationException">Khi <see cref="Day"/> thiếu, hoặc <see cref="Start"/>/<see cref="End"/> không đúng định dạng.</exception>
    /// <exception cref="Shared.DecisionContractSDK.Domain.DomainInvariantException">Khi khung giờ vi phạm bất biến của SDK.</exception>
    internal PeakWindow ToPeakWindow()
    {
        if (Day is not { } day)
        {
            throw new InvalidOperationException("Khung giờ cao điểm thiếu Day.");
        }

        if (!TryParseTime(Start, out var start) || !TryParseTime(End, out var end))
        {
            throw new InvalidOperationException($"Khung giờ cao điểm '{Start}'-'{End}' không đúng định dạng {TimeFormat}.");
        }

        return new PeakWindow(day, start, end);
    }
}
