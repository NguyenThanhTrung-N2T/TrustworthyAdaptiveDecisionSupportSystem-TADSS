namespace TADSS.Runtime.Api.ErrorHandling;

/// <summary>
/// Mô tả một phản hồi lỗi RFC 7807 của API, đầu vào của <see cref="ProblemResponseWriter"/>.
/// </summary>
/// <param name="StatusCode">Mã HTTP trả về.</param>
/// <param name="Code">Mã lỗi wire (<c>ErrorCodes</c> của SDK).</param>
/// <param name="Title">Tiêu đề ngắn của lỗi.</param>
/// <param name="Detail">Mô tả cho client (không chứa chi tiết nội bộ).</param>
/// <param name="RetryAfterSeconds">Số giây nên chờ trước khi thử lại; có giá trị thì body có <c>retryAfterSeconds</c> và response có header <c>Retry-After</c>.</param>
/// <param name="Errors">Lỗi theo trường (tên trường → danh sách thông điệp); <c>null</c> thì body không có <c>errors</c>.</param>
public sealed record ProblemResponse(
    int StatusCode,
    string Code,
    string Title,
    string Detail,
    int? RetryAfterSeconds = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
