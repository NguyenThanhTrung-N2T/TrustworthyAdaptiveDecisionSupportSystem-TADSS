namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Cấu hình xử lý lỗi HTTP của Runtime.Api.
/// </summary>
public class ErrorHandlingOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:ErrorHandling";

    /// <summary>
    /// Số giây client nên chờ trước khi thử lại, trả trong header <c>Retry-After</c> và trường <c>retryAfterSeconds</c>
    /// của lỗi tạm thời (503 <c>PERSISTENCE_UNAVAILABLE</c>, 504 <c>GATEWAY_TIMEOUT</c>). Mặc định 5.
    /// </summary>
    public int RetryAfterSeconds { get; set; } = 5;
}
