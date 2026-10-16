namespace TADSS.Application.Options;

/// <summary>
/// Cấu hình làm nóng đường xử lý quyết định lúc khởi động, trước khi instance nhận request: mở sẵn kết nối PostgreSQL, biên dịch truy vấn,
/// chạy thử mọi strategy qua pipeline. Không có bước này, những request đầu tiên vào một instance lạnh phải gánh toàn bộ chi phí khởi tạo
/// và có thể chậm hàng chục giây khi tải dồn vào. Làm nóng không ghi audit, không ghi cache.
/// </summary>
public class WarmupOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Warmup";

    /// <summary>
    /// Bật làm nóng. Mặc định bật.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Số kết nối PostgreSQL mở sẵn (mỗi kết nối một truy vấn idempotency chỉ đọc chạy song song), 0..100. Mặc định 10.
    /// </summary>
    public int DatabaseConnections { get; set; } = 10;

    /// <summary>
    /// Số vòng chạy thử đường xử lý (mỗi vòng chạy mọi strategy đã đăng ký một lần), 1..100. Mặc định 5.
    /// </summary>
    public int Iterations { get; set; } = 5;

    /// <summary>
    /// Ngân sách thời gian cho cả lượt làm nóng (mili giây), 100..120000. Hết ngân sách thì dừng và khởi động tiếp. Mặc định 15000.
    /// </summary>
    public int TimeoutMs { get; set; } = 15_000;
}
