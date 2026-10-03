namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Kết quả phân loại một exception thành phản hồi lỗi RFC 7807 của API v2 (spec 4.2.3).
/// </summary>
/// <param name="StatusCode">Mã HTTP trả về.</param>
/// <param name="Code">Mã lỗi wire (<c>ErrorCodes</c> của SDK v2).</param>
/// <param name="Title">Tiêu đề ngắn của lỗi.</param>
/// <param name="ExposeMessage">
/// <c>true</c> khi được phép trả nguyên <c>Exception.Message</c> cho client (lỗi do request); <c>false</c> khi phải ẩn chi tiết kỹ thuật.
/// </param>
/// <param name="RetryAfterSeconds">Số giây client nên chờ trước khi thử lại; <c>null</c> khi không áp dụng.</param>
public sealed record ErrorClassification(
    int StatusCode,
    string Code,
    string Title,
    bool ExposeMessage,
    int? RetryAfterSeconds);
