using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Context;
using TADSS.Application.Common;
using TADSS.Runtime.Api.Common;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Middleware to ensure every HTTP request has a canonical correlation ID,
/// propagated to HttpContext.TraceIdentifier, Response Headers, and Serilog LogContext.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = HttpHeaderNames.CorrelationId;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger ?? NullLogger<CorrelationIdMiddleware>.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.Request.Headers[HeaderName].FirstOrDefault();

        // Preserve client-supplied correlation ID as-is when valid (non-blank, at most ContractLimits.MaxCorrelationIdLength chars);
        // otherwise generate a new GUID so an oversized value never reaches TraceIdentifier, log scope, headers or audit
        string correlationId;
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            correlationId = Guid.NewGuid().ToString();
        }
        else if (headerValue.Length > ContractLimits.MaxCorrelationIdLength)
        {
            correlationId = Guid.NewGuid().ToString();
            _logger.LogInformation(
                "Correlation id do client gửi dài {Length} ký tự (tối đa {MaxLength}); thay bằng {CorrelationId}.",
                headerValue.Length,
                ContractLimits.MaxCorrelationIdLength,
                correlationId);
        }
        else
        {
            correlationId = headerValue;
        }

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
