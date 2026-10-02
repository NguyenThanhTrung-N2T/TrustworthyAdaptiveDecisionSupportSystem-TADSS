using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Chuyển exception chưa xử lý thành phản hồi RFC 7807 theo bảng map lỗi v2 (spec 4.2.3) qua <see cref="ExceptionClassifier"/>.
/// Chỉ trả <c>Exception.Message</c> khi lỗi do request (<see cref="ErrorClassification.ExposeMessage"/>); lỗi hạ tầng luôn dùng câu chung theo mã.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
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

        var problemDetails = new ProblemDetails
        {
            Status = classification.StatusCode,
            Title = classification.Title,
            Detail = classification.ExposeMessage ? exception.Message : GenericDetail(classification.StatusCode),
            Instance = httpContext.Request.Path,
            Type = $"https://tadss.smashcourt.vn/errors/{classification.Code.ToLowerInvariant().Replace('_', '-')}"
        };

        problemDetails.Extensions["code"] = classification.Code;
        problemDetails.Extensions["traceId"] = traceId;

        if (classification.RetryAfterSeconds is { } retryAfterSeconds)
        {
            problemDetails.Extensions["retryAfterSeconds"] = retryAfterSeconds;
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var errors = BuildFieldErrors(exception);
        if (errors is not null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = classification.StatusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }

    private static string GenericDetail(int statusCode) => statusCode switch
    {
        StatusCodes.Status503ServiceUnavailable => "Decision audit storage is temporarily unavailable. Please retry later.",
        StatusCodes.Status504GatewayTimeout => "The decision could not be completed within the allowed time. Please retry later.",
        _ => "An unexpected internal server error occurred."
    };

    private static Dictionary<string, string[]>? BuildFieldErrors(Exception exception) => exception switch
    {
        DomainInvariantException { FieldName: { Length: > 0 } field } => new() { [field] = new[] { exception.Message } },
        ContractValidationException { FieldName: { Length: > 0 } field } => new() { [field] = new[] { exception.Message } },
        _ => null
    };
}
