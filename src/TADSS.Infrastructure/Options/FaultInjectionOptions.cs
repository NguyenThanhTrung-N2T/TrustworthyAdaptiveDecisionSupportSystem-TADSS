namespace TADSS.Infrastructure.Options;

/// <summary>
/// Cấu hình tiêm lỗi vào strategy để kiểm thử độ chịu lỗi (A-07). Section <c>Tadss:Testing:FaultInjection</c>. Options luôn được
/// đăng ký và kiểm tra, nhưng decorator chỉ được bọc khi môi trường là <c>Testing</c> <b>và</b> <see cref="Enabled"/> là <c>true</c>;
/// <see cref="Enabled"/> ở môi trường khác chỉ sinh Warning khi khởi động.
/// </summary>
public sealed class FaultInjectionOptions
{
    public const string SectionName = "Tadss:Testing:FaultInjection";

    /// <summary>Bật tiêm lỗi (mặc định tắt).</summary>
    public bool Enabled { get; set; }

    /// <summary>Kiểu lỗi được tiêm (mặc định <see cref="FaultInjectionProfile.None"/>).</summary>
    public FaultInjectionProfile Profile { get; set; } = FaultInjectionProfile.None;

    /// <summary>Thời gian chờ/quay vòng (ms) của các profile <c>Delay</c>, <c>IgnoreToken</c>, <c>BusyCpu</c>; hợp lệ 1..60000 (mặc định 1000).</summary>
    public int DelayMs { get; set; } = 1000;

    /// <summary>Tên strategy bị tiêm lỗi (không phân biệt hoa thường); rỗng hoặc <c>null</c> là mọi strategy.</summary>
    public string? TargetStrategy { get; set; }

    /// <summary><c>true</c> khi lỗi áp cho strategy tên <paramref name="strategyName"/>: <see cref="TargetStrategy"/> để trống (mọi strategy) hoặc khớp tên, không phân biệt hoa thường.</summary>
    public bool Targets(string strategyName) =>
        string.IsNullOrEmpty(TargetStrategy) || string.Equals(TargetStrategy, strategyName, StringComparison.OrdinalIgnoreCase);
}
