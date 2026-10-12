namespace TADSS.Application.Options;

/// <summary>
/// Cấu hình chế độ shadow (A-11): sau khi bản <c>PRIMARY</c> đã lưu, runtime chạy thêm một strategy ứng viên ở nền và ghi bản
/// <c>SHADOW</c> để so sánh, không ảnh hưởng response. Không nạp lại nóng: đổi cấu hình phải khởi động lại.
/// </summary>
public class ShadowOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Shadow";

    /// <summary>
    /// Bật shadow. Mặc định tắt.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Tên strategy chạy shadow (phải có trong registry); bắt buộc khi <see cref="Enabled"/>.
    /// </summary>
    public string Strategy { get; set; } = "";

    /// <summary>
    /// Tỷ lệ request được chạy shadow, trong [0, 1]: 1 luôn chạy, 0 không chạy. Mặc định 1.
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// Sức chứa hàng đợi việc shadow, 1..10000. Đầy thì việc mới bị bỏ (đếm bằng <c>tadss.shadow.dropped</c>). Mặc định 256.
    /// </summary>
    public int QueueCapacity { get; set; } = 256;
}
