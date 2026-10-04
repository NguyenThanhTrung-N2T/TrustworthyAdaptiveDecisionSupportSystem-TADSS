namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Cấu hình bảo mật của Runtime.Api: danh sách API key được chấp nhận cho <c>/api/*</c>, trần kích thước body và việc tin header chuyển tiếp.
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// Tên section cấu hình trong appsettings.json.
    /// </summary>
    public const string SectionName = "Tadss:Security";

    /// <summary>
    /// Các client và mã băm khóa. Rỗng thì mọi request <c>/api/*</c> bị 401; ngoài <c>Development</c>/<c>Testing</c> thì host không khởi động.
    /// </summary>
    public List<ApiKeyClientOptions> ApiKeys { get; set; } = [];

    /// <summary>Cận dưới của <see cref="MaxRequestBodyBytes"/> (byte).</summary>
    public const long MinRequestBodyBytes = 1024;

    /// <summary>Cận trên của <see cref="MaxRequestBodyBytes"/> (byte).</summary>
    public const long MaxRequestBodyBytesLimit = 1_048_576;

    /// <summary>
    /// Trần kích thước body của request (byte, <see cref="MinRequestBodyBytes"/>..<see cref="MaxRequestBodyBytesLimit"/>); vượt thì 413 <c>PAYLOAD_TOO_LARGE</c> trước khi xác thực. Mặc định 64 KB.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 65536;

    /// <summary>
    /// Tin <c>X-Forwarded-For/Proto/Host</c> từ reverse proxy (ngrok). Chỉ bật khi chỉ proxy chạm được ứng dụng; mặc định <c>false</c>
    /// vì khi chạy trực tiếp client có thể giả các header này.
    /// </summary>
    public bool TrustForwardedHeaders { get; set; }
}
