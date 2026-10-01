using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Polly.Timeout;
using Shared.DecisionContractSDK.Common;
using Shared.DecisionContractSDK.Domain;
using TADSS.Runtime.Api.Common;

namespace TADSS.Runtime.Api.Middlewares;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        // Caller Cancellation (Client aborted/closed connection)
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request was canceled by caller. TraceId: {TraceId}, Path: {Path}", traceId, httpContext.Request.Path);
            return true;
        }

        logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}, Message: {Message}", 
            traceId, exception.Message);

        var (statusCode, title, code) = exception switch
        {
            Exception ex when IsPersistenceUnavailable(ex) => (StatusCodes.Status503ServiceUnavailable, "Persistence Unavailable", RuntimeErrorCodes.PersistenceUnavailable),
            DomainInvariantException die => (StatusCodes.Status400BadRequest, "Domain Invariant Violation", die.InvariantCode),
            ContractValidationException cve => (StatusCodes.Status400BadRequest, "Contract Validation Error", cve.ErrorCode),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency Key Reuse Conflict", ErrorCodes.IdempotencyKeyReuse),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency Conflict", ErrorCodes.Conflict),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid Operation", ErrorCodes.BadRequest),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid Request Argument", ErrorCodes.ValidationError),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found", ErrorCodes.NotFound),
            TimeoutRejectedException or TimeoutException or OperationCanceledException => (StatusCodes.Status504GatewayTimeout, "Gateway Timeout", ErrorCodes.GatewayTimeout),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", ErrorCodes.InternalError)
        };

        var detail = statusCode switch
        {
            StatusCodes.Status500InternalServerError => "An unexpected internal server error occurred.",
            StatusCodes.Status503ServiceUnavailable => "Decision audit storage is temporarily unavailable. Please retry later.",
            _ when exception is DbUpdateConcurrencyException => "The target decision record was modified concurrently by another request. Please refresh and retry.",
            _ => exception.Message
        };

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

        if (statusCode is StatusCodes.Status504GatewayTimeout or StatusCodes.Status503ServiceUnavailable)
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
        else if (exception is ContractValidationException contractEx && !string.IsNullOrEmpty(contractEx.FieldName))
        {
            problemDetails.Extensions["errors"] = new Dictionary<string, string[]>
            {
                [contractEx.FieldName] = new[] { contractEx.Message }
            };
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// Lỗi tạm thời của PostgreSQL (mất kết nối, timeout, server quá tải) ở bất kỳ tầng InnerException nào.
    /// EF Core/Npgsql có thể bọc lỗi này trong <see cref="InvalidOperationException"/> hoặc <see cref="DbUpdateException"/>.
    /// </summary>
    private static bool IsPersistenceUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
