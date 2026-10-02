namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Cấu hình kiểm tra yêu cầu đánh giá v2 (P-01): giới hạn thời lượng của mỗi item. SDK chỉ bảo đảm
/// <c>End &gt; Start</c>; giới hạn cụ thể là chính sách của runtime.
/// </summary>
public class ValidationOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Validation";

    /// <summary>
    /// Thời lượng tối thiểu (phút) của một item (tính cả biên); phải lớn hơn 0.
    /// </summary>
    public int MinItemMinutes { get; set; } = 30;

    /// <summary>
    /// Thời lượng tối đa (phút) của một item (tính cả biên); không nhỏ hơn <see cref="MinItemMinutes"/>.
    /// </summary>
    public int MaxItemMinutes { get; set; } = 720;
}
