namespace TADSS.Application.Exceptions;

/// <summary>
/// Lưu trữ tạm thời không khả dụng (mất kết nối, timeout hoặc lỗi PostgreSQL có thể thử lại). Được map thành
/// 503 <c>PERSISTENCE_UNAVAILABLE</c> kèm <c>Retry-After</c>; khác với vi phạm ràng buộc hay lỗi lập trình (500).
/// </summary>
public sealed class PersistenceUnavailableException : Exception
{
    /// <summary>
    /// Khởi tạo lỗi lưu trữ không khả dụng.
    /// </summary>
    /// <param name="message">Mô tả lỗi.</param>
    /// <param name="innerException">Exception gốc từ driver/cơ sở dữ liệu.</param>
    public PersistenceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
