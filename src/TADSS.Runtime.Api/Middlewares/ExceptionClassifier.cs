using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Exceptions;
using TADSS.Infrastructure.Persistence;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Phân loại exception thành phản hồi lỗi v2 theo bảng map lỗi của spec 4.2.3. Thuần túy, không I/O, không ghi log.
/// Lỗi persistence được kiểm tra trước mọi nhánh khác để lỗi hạ tầng bị bọc trong exception khác vẫn thành 503.
/// <see cref="ContractValidationException"/> chỉ được mang mã <c>VALIDATION_ERROR</c> hoặc <c>MISSING_HEADER</c> ra wire (tập đóng).
/// Được <c>GlobalExceptionHandler</c> dùng để dựng phản hồi lỗi.
/// </summary>
public static class ExceptionClassifier
{
    private const int RetryAfterSeconds = 5;

    /// <summary>
    /// Phân loại <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">Exception cần phân loại.</param>
    /// <param name="requestAborted">Client đã hủy/đóng kết nối (<c>HttpContext.RequestAborted</c>).</param>
    /// <returns>Phân loại lỗi; <c>null</c> khi client đã hủy request và không cần ghi response.</returns>
    public static ErrorClassification? Classify(Exception exception, bool requestAborted)
    {
        if (exception is OperationCanceledException && requestAborted)
        {
            return null;
        }

        if (PersistenceFailureDetector.IsRetryable(exception))
        {
            return new ErrorClassification(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.PersistenceUnavailable,
                "Persistence Unavailable",
                ExposeMessage: false,
                RetryAfterSeconds);
        }

        return exception switch
        {
            ContractValidationException { ErrorCode: ErrorCodes.MissingHeader } => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.MissingHeader, "Missing Header", ExposeMessage: true, null),
            // Tập mã đóng: ErrorCode lạ của ContractValidationException (kể cả INTERNAL_ERROR) luôn là VALIDATION_ERROR
            ContractValidationException => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, "Validation Error", ExposeMessage: true, null),
            DomainInvariantException => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.DomainInvariantViolation, "Domain Invariant Violation", ExposeMessage: true, null),
            DecisionNotFoundException => new ErrorClassification(
                StatusCodes.Status404NotFound, ErrorCodes.DecisionNotFound, "Decision Not Found", ExposeMessage: true, null),
            IdempotencyConflictException => new ErrorClassification(
                StatusCodes.Status409Conflict, ErrorCodes.IdempotencyKeyReuse, "Idempotency Key Reuse", ExposeMessage: true, null),
            TimeoutRejectedException => new ErrorClassification(
                StatusCodes.Status504GatewayTimeout, ErrorCodes.GatewayTimeout, "Gateway Timeout", ExposeMessage: false, RetryAfterSeconds),
            _ => new ErrorClassification(
                StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Internal Server Error", ExposeMessage: false, null),
        };
    }
}
