namespace Shared.DecisionContractSDK.V2.Domain;

/// <summary>
/// Lịch cao điểm của chi nhánh — nơi duy nhất định nghĩa "giờ cao điểm", dùng chung cho router và strategy.
/// Giá trị mặc định do runtime cấu hình (<c>Tadss:Calendar:PeakWindows</c>, gói 5.2b).
/// </summary>
public sealed class BranchCalendar
{
    /// <summary>Các khung giờ cao điểm.</summary>
    public IReadOnlyList<PeakWindow> PeakWindows { get; }

    /// <summary>Khởi tạo lịch từ danh sách khung giờ cao điểm (có thể rỗng: không có giờ cao điểm).</summary>
    /// <param name="peakWindows">Các khung giờ cao điểm, không có phần tử null.</param>
    public BranchCalendar(IEnumerable<PeakWindow> peakWindows)
    {
        ArgumentNullException.ThrowIfNull(peakWindows);

        var windows = peakWindows.ToArray();
        if (windows.Any(window => window is null))
        {
            throw new ArgumentException("Peak windows must not contain null elements.", nameof(peakWindows));
        }

        PeakWindows = Array.AsReadOnly(windows);
    }

    /// <summary>
    /// Reservation có rơi vào giờ cao điểm không: xét thời điểm bắt đầu sớm nhất, quy về giờ chi nhánh.
    /// </summary>
    /// <param name="reservation">Reservation cần xét.</param>
    public bool IsPeak(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var branchLocal = reservation.ToBranchLocal(reservation.EarliestStart);
        return PeakWindows.Any(window => window.Contains(branchLocal));
    }
}
