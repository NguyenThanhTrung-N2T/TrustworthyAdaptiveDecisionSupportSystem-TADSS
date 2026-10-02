using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shared.DecisionContractSDK.V2.Domain;

namespace TADSS.Runtime.Api.V2.Services.Strategies;

/// <summary>
/// Dựng dấu vân tay tham số (<c>optionsFingerprint</c>, spec 4.2.3, B-08) của một strategy: mỗi tham số ghi thành một
/// dòng <c>name=value</c> theo đúng thứ tự gọi, nối bằng <c>\n</c>, băm SHA-256 và lấy 16 ký tự hex thường đầu tiên.
/// Danh sách tham số do nơi gọi khai báo tường minh (không serialize POCO bằng reflection) nên thêm/bớt/đổi thứ tự
/// tham số là thay đổi có chủ đích. Số được ghi theo culture bất biến và bỏ số 0 vô nghĩa (cùng quy tắc với
/// <see cref="RequestHasher"/>), nên <c>0.50</c> và <c>0.5</c> cho cùng fingerprint.
/// </summary>
public sealed class OptionsFingerprintBuilder
{
    private const int FingerprintLength = 16;

    private readonly List<string> _lines = [];

    /// <summary>
    /// Thêm một tham số thập phân.
    /// </summary>
    public void Add(string name, decimal value) => _lines.Add($"{name}={Format(value)}");

    /// <summary>
    /// Thêm một tham số nguyên.
    /// </summary>
    public void Add(string name, int value) => _lines.Add($"{name}={value.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// Thêm lịch cao điểm: số cửa sổ rồi từng cửa sổ sắp theo (ngày trong tuần, giờ bắt đầu, giờ kết thúc) nên thứ tự
    /// khai báo cửa sổ không ảnh hưởng fingerprint.
    /// </summary>
    public void AddCalendar(BranchCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        _lines.Add($"PeakWindowCount={calendar.PeakWindows.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (var window in calendar.PeakWindows
                     .OrderBy(window => (int)window.Day)
                     .ThenBy(window => window.Start)
                     .ThenBy(window => window.End))
        {
            _lines.Add($"PeakWindow={window.Day}|{FormatTime(window.Start)}|{FormatTime(window.End)}");
        }
    }

    /// <summary>
    /// Tính fingerprint từ các tham số đã thêm.
    /// </summary>
    /// <returns>16 ký tự hex thường.</returns>
    public string Build()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', _lines)));
        return Convert.ToHexString(hash).ToLowerInvariant()[..FingerprintLength];
    }

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Chuỗi bất biến theo culture của số thập phân, bỏ số 0 vô nghĩa ở phần thập phân (<c>0.50</c> → <c>0.5</c>,
    /// <c>3000000.00</c> → <c>3000000</c>, <c>0.00</c> → <c>0</c>).
    /// </summary>
    private static string Format(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text is "-0" or "" ? "0" : text;
    }
}
