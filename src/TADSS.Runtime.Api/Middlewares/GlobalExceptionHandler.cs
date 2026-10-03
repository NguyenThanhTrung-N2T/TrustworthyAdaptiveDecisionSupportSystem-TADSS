using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using TADSS.Runtime.Api.ErrorHandling;
using TADSS.Runtime.Api.Options;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Chuyển exception chưa xử lý thành phản hồi RFC 7807 theo bảng map lỗi (spec 4.2.3) qua <see cref="ExceptionClassifier"/>.
/// Header <c>Retry-After</c> của lỗi tạm thời lấy từ <see cref="ErrorHandlingOptions.RetryAfterSeconds"/>.
/// Body do <see cref="ProblemResponseWriter"/> dựng (cùng đường với các phản hồi từ chối của middleware).
/// Chỉ trả <c>Exception.Message</c> khi lỗi do request (<see cref="ErrorClassification.ExposeMessage"/>); lỗi hạ tầng luôn dùng câu chung theo mã.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IOptions<ErrorHandlingOptions> options) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        var classification = ExceptionClassifier.Classify(exception, httpContext.RequestAborted.IsCancellationRequested);

        // Caller Cancellation (Client aborted/closed connection): không còn ai đọc response
        if (classification is null)
        {
            logger.LogInformation("Request was canceled by caller. TraceId: {TraceId}, Path: {Path}", traceId, httpContext.Request.Path);
            return true;
        }

        if (classification.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request failed with {StatusCode} {Code}. TraceId: {TraceId}, Path: {Path}",
                classification.StatusCode, classification.Code, traceId, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Request rejected with {StatusCode} {Code}. TraceId: {TraceId}, Path: {Path}, Message: {Message}",
                classification.StatusCode, classification.Code, traceId, httpContext.Request.Path, exception.Message);
        }

        await ProblemResponseWriter.WriteAsync(
            httpContext,
            new ProblemResponse(
                classification.StatusCode,
                classification.Code,
                classification.Title,
                classification.ExposeMessage ? exception.Message : ExceptionClassifier.GenericDetail(classification.StatusCode),
                classification.HasRetryAfter ? options.Value.RetryAfterSeconds : null,
                ExceptionClassifier.FieldErrors(exception)),
            cancellationToken);

        return true;
    }
}
