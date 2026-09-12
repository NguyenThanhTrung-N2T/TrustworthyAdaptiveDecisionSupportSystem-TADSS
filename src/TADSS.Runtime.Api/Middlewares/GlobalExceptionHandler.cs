using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TADSS.Runtime.Api.Middlewares;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // Log chi tiết vào Serilog nội bộ
        logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}, Message: {Message}", 
            traceId, exception.Message);

        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid Request Argument"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
            TimeoutException => (StatusCodes.Status504GatewayTimeout, "Decision Execution Timeout"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        // Masking: Không để lộ message chi tiết của DB/Hệ thống khi gặp lỗi 500
        var detail = statusCode == StatusCodes.Status500InternalServerError
            ? "An unexpected internal server error occurred."
            : exception.Message;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
