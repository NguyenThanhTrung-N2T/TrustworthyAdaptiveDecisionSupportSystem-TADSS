using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace TADSS.Runtime.Api.ErrorHandling;

/// <summary>
/// Bộ ghi duy nhất dựng body <c>application/problem+json</c> của API (spec 4.2.4): <c>GlobalExceptionHandler</c> (exception) và
/// các middleware từ chối request (401, 413, 429) đều đi qua đây nên cùng một dạng body (<c>code</c>, <c>traceId</c>, <c>retryAfterSeconds</c>, <c>errors</c>).
/// Không ném exception: middleware từ chối request gọi trực tiếp, không tốn chi phí exception cho mỗi request bị từ chối.
/// </summary>
public static class ProblemResponseWriter
{
    /// <summary>Media type của mọi phản hồi lỗi.</summary>
    public const string ContentType = "application/problem+json";

    /// <summary>
    /// Ghi <paramref name="problem"/> vào response: status, <c>Content-Type</c>, body, và header <c>Retry-After</c> khi có <see cref="ProblemResponse.RetryAfterSeconds"/>.
    /// <c>traceId</c> là <see cref="HttpContext.TraceIdentifier"/> (correlation id đã chuẩn hóa), <c>instance</c> là path của request.
    /// </summary>
    public static Task WriteAsync(HttpContext context, ProblemResponse problem, CancellationToken cancellationToken)
    {
        var details = new ProblemDetails
        {
            Status = problem.StatusCode,
            Title = problem.Title,
            Detail = problem.Detail,
            Instance = context.Request.Path,
            Type = $"https://tadss.smashcourt.vn/errors/{problem.Code.ToLowerInvariant().Replace('_', '-')}"
        };

        details.Extensions["code"] = problem.Code;
        details.Extensions["traceId"] = context.TraceIdentifier;

        if (problem.RetryAfterSeconds is { } retryAfterSeconds)
        {
            details.Extensions["retryAfterSeconds"] = retryAfterSeconds;
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        if (problem.Errors is not null)
        {
            details.Extensions["errors"] = problem.Errors;
        }

        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = ContentType;
        return context.Response.WriteAsJsonAsync(details, options: (JsonSerializerOptions?)null, contentType: ContentType, cancellationToken: cancellationToken);
    }
}
