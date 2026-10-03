using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Exceptions;

namespace TADSS.Runtime.Api.ErrorHandling;

/// <summary>
/// Phân loại exception thành phản hồi lỗi theo bảng map lỗi của spec 4.2.3. Thuần túy, không I/O, không ghi log.
/// Chỉ biết exception của SDK và Application: adapter hạ tầng đã dịch lỗi Npgsql/Polly thành <see cref="PersistenceUnavailableException"/>
/// và <see cref="DecisionTimeoutException"/>, nên exception thô của thư viện hạ tầng lọt tới đây là lỗi lập trình (500).
/// <see cref="PersistenceUnavailableException"/> được tìm ở mọi tầng inner (kể cả mọi nhánh của <see cref="AggregateException"/>) và kiểm tra trước mọi nhánh khác.
/// <see cref="ContractValidationException"/> chỉ được mang mã <c>VALIDATION_ERROR</c> hoặc <c>MISSING_HEADER</c> ra wire (tập đóng).
/// Được <c>GlobalExceptionHandler</c> dùng để dựng phản hồi lỗi.
/// </summary>
public static class ExceptionClassifier
{
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

        if (ContainsPersistenceUnavailable(exception))
        {
            return new ErrorClassification(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.PersistenceUnavailable,
                "Persistence Unavailable",
                ExposeMessage: false,
                HasRetryAfter: true);
        }

        return exception switch
        {
            ContractValidationException { ErrorCode: ErrorCodes.MissingHeader } => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.MissingHeader, "Missing Header", ExposeMessage: true, HasRetryAfter: false),
            // Tập mã đóng: ErrorCode lạ của ContractValidationException (kể cả INTERNAL_ERROR) luôn là VALIDATION_ERROR
            ContractValidationException => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, "Validation Error", ExposeMessage: true, HasRetryAfter: false),
            DomainInvariantException => new ErrorClassification(
                StatusCodes.Status400BadRequest, ErrorCodes.DomainInvariantViolation, "Domain Invariant Violation", ExposeMessage: true, HasRetryAfter: false),
            DecisionNotFoundException => new ErrorClassification(
                StatusCodes.Status404NotFound, ErrorCodes.DecisionNotFound, "Decision Not Found", ExposeMessage: true, HasRetryAfter: false),
            IdempotencyConflictException => new ErrorClassification(
                StatusCodes.Status409Conflict, ErrorCodes.IdempotencyKeyReuse, "Idempotency Key Reuse", ExposeMessage: true, HasRetryAfter: false),
            DecisionStateConflictException conflict => new ErrorClassification(
                StatusCodes.Status409Conflict, conflict.ErrorCode, "Decision State Conflict", ExposeMessage: true, HasRetryAfter: false),
            DecisionTimeoutException => new ErrorClassification(
                StatusCodes.Status504GatewayTimeout, ErrorCodes.GatewayTimeout, "Gateway Timeout", ExposeMessage: false, HasRetryAfter: true),
            _ => new ErrorClassification(
                StatusCodes.Status500InternalServerError, ErrorCodes.InternalError, "Internal Server Error", ExposeMessage: false, HasRetryAfter: false),
        };
    }

    private static bool ContainsPersistenceUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case PersistenceUnavailableException:
                    return true;
                case AggregateException aggregate when aggregate.InnerExceptions.Any(ContainsPersistenceUnavailable):
                    // AggregateException.InnerException chỉ là nhánh đầu tiên nên phải duyệt cả InnerExceptions
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Câu <c>detail</c> chung theo status cho lỗi không được phép lộ <c>Exception.Message</c> (<see cref="ErrorClassification.ExposeMessage"/> = <c>false</c>).
    /// </summary>
    public static string GenericDetail(int statusCode) => statusCode switch
    {
        StatusCodes.Status503ServiceUnavailable => "Decision audit storage is temporarily unavailable. Please retry later.",
        StatusCodes.Status504GatewayTimeout => "The decision could not be completed within the allowed time. Please retry later.",
        _ => "An unexpected internal server error occurred."
    };

    /// <summary>
    /// Lỗi theo trường (<c>errors</c> của problem+json) khi exception gắn với một trường cụ thể; <c>null</c> nếu không.
    /// </summary>
    public static Dictionary<string, string[]>? FieldErrors(Exception exception) => exception switch
    {
        DomainInvariantException { FieldName: { Length: > 0 } field } => new() { [field] = new[] { exception.Message } },
        ContractValidationException { FieldName: { Length: > 0 } field } => new() { [field] = new[] { exception.Message } },
        _ => null
    };
}
