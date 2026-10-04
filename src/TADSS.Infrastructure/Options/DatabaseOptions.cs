namespace TADSS.Infrastructure.Options;

/// <summary>
/// Cấu hình vận hành cơ sở dữ liệu của TADSS.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Database";

    /// <summary>
    /// Chuỗi kết nối PostgreSQL, lấy từ <c>ConnectionStrings:DefaultConnection</c> của cấu hình cuối cùng trong DI (không bind từ section
    /// <see cref="SectionName"/>). DbContext và health check đọc qua đây; thiếu hoặc sai thì host không khởi động.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Tự áp migration EF Core khi khởi động. Chỉ bật cho database thử dùng một lần (verify-expose.sh, Testcontainers, lab E-LOCAL ở gói 5.5);
    /// không bật cho Neon hay staging, và docker-compose.yml luôn đặt false — migration ở đó do người vận hành chủ động chạy (dotnet ef database update).
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
