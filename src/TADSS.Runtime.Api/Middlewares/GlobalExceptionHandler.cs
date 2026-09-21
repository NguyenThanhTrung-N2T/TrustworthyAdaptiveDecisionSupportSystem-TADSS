using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shared.DecisionContractSDK.Domain;

namespace TADSS.Runtime.Api.Middlewares;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}, Message: {Message}", 
            traceId, exception.Message);

        var (statusCode, title, code) = exception switch
        {
            DomainInvariantException die => (StatusCodes.Status400BadRequest, "Domain Invariant Violation", die.InvariantCode),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency Key Reuse Conflict", "IDEMPOTENCY_KEY_REUSE"),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid Operation", "INVALID_OPERATION"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid Request Argument", "INVALID_ARGUMENT"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found", "RESOURCE_NOT_FOUND"),
            TimeoutException or OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "Gateway Timeout", "GATEWAY_TIMEOUT"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "INTERNAL_SERVER_ERROR")
        };

        var detail = statusCode == StatusCodes.Status500InternalServerError
            ? "An unexpected internal server error occurred."
            : exception.Message;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = $"https://tadss.smashcourt.vn/errors/{code.ToLowerInvariant().Replace('_', '-')}"
        };

        problemDetails.Extensions["code"] = code;
        problemDetails.Extensions["traceId"] = traceId;

        if (statusCode == StatusCodes.Status504GatewayTimeout)
        {
            problemDetails.Extensions["retryAfterSeconds"] = 5;
        }

        if (exception is DomainInvariantException domainEx && !string.IsNullOrEmpty(domainEx.FieldName))
        {
            problemDetails.Extensions["errors"] = new Dictionary<string, string[]>
            {
                [domainEx.FieldName] = new[] { domainEx.Message }
            };
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
