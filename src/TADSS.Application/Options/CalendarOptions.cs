using Shared.DecisionContractSDK.Domain;

namespace TADSS.Application.Options;

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
