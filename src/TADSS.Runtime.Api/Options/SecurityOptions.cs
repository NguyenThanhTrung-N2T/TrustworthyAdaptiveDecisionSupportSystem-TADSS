namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Cấu hình bảo mật của Runtime.Api: danh sách API key được chấp nhận cho <c>/api/*</c>.
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
}
