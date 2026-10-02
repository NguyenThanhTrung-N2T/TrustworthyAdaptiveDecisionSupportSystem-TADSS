using Npgsql;
using TADSS.Runtime.Api.V2.Middlewares;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Nhận diện lỗi lưu trữ có thể thử lại (PostgreSQL tạm thời không khả dụng) ở bất kỳ tầng <c>InnerException</c> nào.
/// Dùng chung cho handler lỗi v1 và bộ phân loại lỗi v2; EF Core/Npgsql thường bọc lỗi gốc trong
/// <see cref="InvalidOperationException"/> hoặc <c>DbUpdateException</c>.
/// </summary>
public static class PersistenceFailureDetector
{
    /// <summary>SqlState lỗi tuần tự hóa giao dịch (serialization failure).</summary>
    private const string SerializationFailure = "40001";

    /// <summary>SqlState deadlock.</summary>
    private const string DeadlockDetected = "40P01";

    /// <summary>
    /// Kiểm tra <paramref name="exception"/> (và toàn bộ chuỗi <c>InnerException</c>) có phải lỗi lưu trữ có thể thử lại.
    /// Đúng khi gặp <see cref="PersistenceUnavailableException"/>, <see cref="NpgsqlException"/> có
    /// <c>IsTransient</c>, hoặc <see cref="PostgresException"/> với SqlState <c>40001</c>/<c>40P01</c> (kiểm tường minh).
    /// Vi phạm ràng buộc (ví dụ <c>23505</c>) không phải lỗi có thể thử lại.
    /// </summary>
    /// <param name="exception">Exception cần kiểm tra.</param>
    /// <returns><c>true</c> nếu nên trả 503 kèm <c>Retry-After</c>.</returns>
    public static bool IsRetryable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PersistenceUnavailableException:
                case NpgsqlException { IsTransient: true }:
                case PostgresException { SqlState: SerializationFailure or DeadlockDetected }:
                    return true;
            }
        }

        return false;
    }
}
