using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Một khung giờ cao điểm theo giờ địa phương của chi nhánh: <c>[Start, End)</c> trong ngày <see cref="Day"/>.
/// </summary>
public sealed class PeakWindow
{
    /// <summary>Ngày trong tuần.</summary>
    public DayOfWeek Day { get; }

    /// <summary>Giờ bắt đầu (tính cả).</summary>
    public TimeOnly Start { get; }

    /// <summary>Giờ kết thúc (không tính), sau <see cref="Start"/>.</summary>
    public TimeOnly End { get; }

    /// <summary>Khởi tạo khung giờ cao điểm.</summary>
    /// <param name="day">Ngày trong tuần.</param>
    /// <param name="start">Giờ bắt đầu.</param>
    /// <param name="end">Giờ kết thúc, sau <paramref name="start"/> trong cùng ngày.</param>
    /// <exception cref="DomainInvariantException">Khi ngày không hợp lệ hoặc <paramref name="end"/> không sau <paramref name="start"/>.</exception>
    public PeakWindow(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        if (!Enum.IsDefined(day) || end <= start)
        {
            throw new DomainInvariantException(
                "INVALID_PEAK_WINDOW",
                $"Peak window {day} {start:HH\\:mm}-{end:HH\\:mm} is invalid; End must be after Start on the same day.",
                nameof(PeakWindow));
        }

        Day = day;
        Start = start;
        End = end;
    }

    /// <summary>Thời điểm (đã quy về giờ chi nhánh) có nằm trong khung giờ này không.</summary>
    /// <param name="branchLocal">Thời điểm theo giờ địa phương của chi nhánh.</param>
    public bool Contains(DateTimeOffset branchLocal)
    {
        var time = TimeOnly.FromTimeSpan(branchLocal.TimeOfDay);
        return branchLocal.DayOfWeek == Day && time >= Start && time < End;
    }
}
