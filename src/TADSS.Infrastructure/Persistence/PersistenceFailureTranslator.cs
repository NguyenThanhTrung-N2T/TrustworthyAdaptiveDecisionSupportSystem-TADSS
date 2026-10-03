using Microsoft.Extensions.Logging;
using TADSS.Application.Exceptions;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Dịch lỗi lưu trữ của adapter EF Core thành exception của Application, dùng chung cho các store. Lỗi có thể thử lại
/// (theo <see cref="PersistenceFailureDetector.IsRetryable"/>) thành <see cref="PersistenceUnavailableException"/> (503); nếu token của caller
/// đã hủy thì đó là hủy của caller (client đóng kết nối), thành <see cref="OperationCanceledException"/> để không ghi log 503 giả.
/// </summary>
internal static class PersistenceFailureTranslator
{
    private const string UnavailableMessage = "Kho lưu trữ quyết định tạm thời không khả dụng; vui lòng thử lại sau.";
    private const string CanceledMessage = "Thao tác lưu trữ bị hủy theo yêu cầu của caller.";

    /// <summary>
    /// Trả exception thay thế cho <paramref name="exception"/>, hoặc <c>null</c> nếu giữ nguyên exception gốc (hủy sẵn có, hoặc lỗi không thể thử lại
    /// như CHECK/FK/unique). Exception thay thế luôn giữ exception gốc làm <c>InnerException</c>.
    /// </summary>
    /// <param name="exception">Exception bắt được từ pha SQL.</param>
    /// <param name="cancellationToken">Token của caller.</param>
    public static Exception? Translate(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException || !PersistenceFailureDetector.IsRetryable(exception))
        {
            return null;
        }

        return cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(CanceledMessage, exception, cancellationToken)
            : new PersistenceUnavailableException(UnavailableMessage, exception);
    }

    /// <summary>
    /// Chạy <paramref name="operation"/> và dịch lỗi theo <see cref="Translate"/>; chỉ log Warning (loại lỗi, không dữ liệu) khi dịch thành 503.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            var translated = Translate(ex, cancellationToken);
            if (translated is null)
            {
                throw;
            }

            if (translated is PersistenceUnavailableException)
            {
                logger.LogWarning("Kho audit tạm thời không khả dụng ({ExceptionType}).", ex.GetType().Name);
            }

            throw translated;
        }
    }
}
