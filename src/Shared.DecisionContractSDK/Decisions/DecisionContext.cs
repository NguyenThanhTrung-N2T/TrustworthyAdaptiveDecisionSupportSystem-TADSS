using Shared.DecisionContractSDK.Domain;

namespace Shared.DecisionContractSDK.Decisions;

/// <summary>
/// Ngữ cảnh runtime cung cấp cho strategy. Strategy lấy thời điểm đánh giá từ đây, không đọc đồng hồ hệ thống,
/// để kết quả tất định và test được.
/// </summary>
public sealed class DecisionContext
{
    /// <summary>Thời điểm đánh giá (UTC, offset 0).</summary>
    public DateTimeOffset EvaluatedAtUtc { get; }

    /// <summary>Lịch cao điểm của chi nhánh.</summary>
    public BranchCalendar Calendar { get; }

    /// <summary>Khởi tạo ngữ cảnh.</summary>
    /// <param name="evaluatedAtUtc">Thời điểm đánh giá, offset phải bằng 0.</param>
    /// <param name="calendar">Lịch cao điểm của chi nhánh.</param>
    /// <exception cref="ArgumentException">Khi <paramref name="evaluatedAtUtc"/> không ở UTC.</exception>
    public DecisionContext(DateTimeOffset evaluatedAtUtc, BranchCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (evaluatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("EvaluatedAtUtc must have a zero offset (UTC).", nameof(evaluatedAtUtc));
        }

        EvaluatedAtUtc = evaluatedAtUtc;
        Calendar = calendar;
    }
}
