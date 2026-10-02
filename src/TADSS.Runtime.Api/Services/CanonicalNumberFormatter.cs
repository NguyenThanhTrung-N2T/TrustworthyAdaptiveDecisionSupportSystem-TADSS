using System.Globalization;

namespace TADSS.Runtime.Api.Services;

/// <summary>
/// Quy tắc duy nhất để ghi số thập phân vào dữ liệu canonical dùng cho băm (<see cref="RequestHasher"/> của
/// <c>request_hash</c> và <c>OptionsFingerprintBuilder</c> của <c>optionsFingerprint</c>). Đổi quy tắc này làm lệch
/// mọi hash/khóa cache đã lưu, nên chỉ được có một hiện thực.
/// </summary>
public static class CanonicalNumberFormatter
{
    /// <summary>
    /// Chuỗi bất biến theo culture của số thập phân, bỏ số 0 vô nghĩa ở phần thập phân (<c>228000.00</c> → <c>228000</c>,
    /// <c>0.50</c> → <c>0.5</c>, <c>0.00</c> → <c>0</c>). <see cref="decimal.ToString(IFormatProvider)"/> không bao giờ
    /// dùng ký hiệu mũ nên chỉ cần cắt đuôi.
    /// </summary>
    public static string Format(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text is "-0" or "" ? "0" : text;
    }
}
