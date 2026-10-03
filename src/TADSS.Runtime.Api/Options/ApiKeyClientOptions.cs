namespace TADSS.Runtime.Api.Options;

/// <summary>
/// Một client được phép gọi <c>/api/*</c>: định danh và mã băm SHA-256 của API key (không bao giờ lưu khóa gốc).
/// </summary>
public class ApiKeyClientOptions
{
    /// <summary>Định danh client (1..64 ký tự <c>A-Z a-z 0-9 . _ -</c>, không phân biệt hoa thường khi so trùng); xuất hiện trong log và phân vùng rate limit.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>SHA-256 (UTF-8) của API key, dạng hex 64 ký tự, không phân biệt hoa thường.</summary>
    public string KeySha256 { get; set; } = string.Empty;
}
