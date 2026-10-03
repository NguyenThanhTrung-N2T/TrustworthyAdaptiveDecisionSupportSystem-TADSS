namespace TADSS.Application.Exceptions;

/// <summary>
/// Pipeline strategy vượt ngân sách thời gian toàn cục (Outer timeout). Được map thành 504 <c>GATEWAY_TIMEOUT</c> kèm
/// <c>Retry-After</c>; message không chứa chi tiết nội bộ nên không trả cho client, exception gốc của thư viện chịu lỗi
/// được giữ ở <see cref="Exception.InnerException"/> để ghi log.
/// </summary>
public sealed class DecisionTimeoutException : Exception
{
    /// <summary>
    /// Khởi tạo lỗi vượt ngân sách thời gian của pipeline.
    /// </summary>
    /// <param name="message">Mô tả lỗi (tiếng Việt, không chứa chi tiết nội bộ).</param>
    /// <param name="innerException">Exception gốc của thư viện chịu lỗi.</param>
    public DecisionTimeoutException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
