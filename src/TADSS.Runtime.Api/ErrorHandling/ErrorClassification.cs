namespace TADSS.Runtime.Api.ErrorHandling;

/// <summary>
/// Kết quả phân loại một exception thành phản hồi lỗi RFC 7807 của API (spec 4.2.3).
/// </summary>
/// <param name="StatusCode">Mã HTTP trả về.</param>
/// <param name="Code">Mã lỗi wire (<c>ErrorCodes</c> của SDK).</param>
/// <param name="Title">Tiêu đề ngắn của lỗi.</param>
/// <param name="ExposeMessage">
/// <c>true</c> khi được phép trả nguyên <c>Exception.Message</c> cho client (lỗi do request); <c>false</c> khi phải ẩn chi tiết kỹ thuật.
/// </param>
/// <param name="HasRetryAfter">
/// <c>true</c> khi lỗi tạm thời, client nên thử lại sau; số giây do <c>GlobalExceptionHandler</c> điền từ <c>ErrorHandlingOptions</c>.
/// </param>
public sealed record ErrorClassification(
    int StatusCode,
    string Code,
    string Title,
    bool ExposeMessage,
    bool HasRetryAfter);
