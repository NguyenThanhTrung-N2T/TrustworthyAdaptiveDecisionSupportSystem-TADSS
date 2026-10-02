using System.Globalization;
using Shared.DecisionContractSDK.V2.Domain;

namespace TADSS.Runtime.Api.V2.Configuration;

/// <summary>
/// Cấu hình lịch cao điểm của chi nhánh (<c>Tadss:Calendar</c>). Danh sách rỗng nghĩa là dùng
/// <see cref="DefaultPeakWindows"/> (SAS 4.3).
/// </summary>
public class CalendarOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Calendar";

    /// <summary>
    /// Các khung giờ cao điểm. Cố ý để rỗng (không khởi tạo sẵn mặc định) vì binder của .NET nối thêm phần tử
    /// vào list có sẵn thay vì thay thế; mặc định được áp dụng trong <see cref="ToCalendar"/>.
    /// </summary>
    public List<PeakWindowOptions> PeakWindows { get; set; } = [];

    /// <summary>
    /// Lịch cao điểm mặc định (SAS 4.3): thứ Hai–thứ Sáu 17:00–21:00, thứ Bảy–Chủ Nhật 08:00–21:00.
    /// Mỗi lần gọi trả về danh sách mới để tránh chia sẻ đối tượng cấu hình có thể sửa.
    /// </summary>
    public static IReadOnlyList<PeakWindowOptions> DefaultPeakWindows =>
    [
        new() { Day = DayOfWeek.Monday, Start = "17:00", End = "21:00" },
        new() { Day = DayOfWeek.Tuesday, Start = "17:00", End = "21:00" },
        new() { Day = DayOfWeek.Wednesday, Start = "17:00", End = "21:00" },
        new() { Day = DayOfWeek.Thursday, Start = "17:00", End = "21:00" },
        new() { Day = DayOfWeek.Friday, Start = "17:00", End = "21:00" },
        new() { Day = DayOfWeek.Saturday, Start = "08:00", End = "21:00" },
        new() { Day = DayOfWeek.Sunday, Start = "08:00", End = "21:00" },
    ];

    /// <summary>
    /// Dựng <see cref="BranchCalendar"/> từ cấu hình (hoặc từ <see cref="DefaultPeakWindows"/> khi danh sách rỗng).
    /// Cấu hình sai đã bị <c>CalendarOptionsValidator</c> chặn lúc khởi động.
    /// </summary>
    public BranchCalendar ToCalendar()
    {
        IReadOnlyList<PeakWindowOptions> source = PeakWindows.Count == 0 ? DefaultPeakWindows : PeakWindows;
        return new BranchCalendar(source.Select(window => window.ToPeakWindow()));
    }
}

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
    /// Chuyển thành <see cref="PeakWindow"/> của SDK v2.
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
