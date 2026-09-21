using Serilog.Context;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Middleware to ensure every HTTP request has a canonical correlation ID,
/// propagated to HttpContext.TraceIdentifier, Response Headers, and Serilog LogContext.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.Request.Headers[HeaderName].FirstOrDefault();

        // Preserve client-supplied correlation ID as-is; if missing or whitespace, generate a new GUID
        var correlationId = string.IsNullOrWhiteSpace(headerValue)
            ? Guid.NewGuid().ToString()
            : headerValue;

        // 1. Set canonical TraceIdentifier
        context.TraceIdentifier = correlationId;

        // 2. Attach correlation ID to response headers
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
            {
                context.Response.Headers[HeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        // 3. Push to Serilog LogContext with guaranteed disposal on request completion
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
