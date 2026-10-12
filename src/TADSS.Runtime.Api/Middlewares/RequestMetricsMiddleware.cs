using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;
using TADSS.Application.Common;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Ghi <c>tadss.decision.duration</c> cho mọi request <c>/api/*</c> với <b>status cuối</b>: đứng ngoài exception handler nên 409/503/504 do handler dựng
/// được thấy đúng. Nhãn <c>endpoint</c> là mẫu route (<c>POST api/v2/decisions</c>), không bao giờ là đường dẫn thật (chứa <c>decisionId</c>);
/// request bị từ chối trước routing (401/413/429) là <c>"{METHOD} unmatched"</c>. Health và Swagger không được đo.
/// </summary>
public partial class RequestMetricsMiddleware(RequestDelegate next, TadssMetrics metrics, TimeProvider timeProvider)
{
    private const string UnmatchedTemplate = "unmatched";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!SecurityPaths.IsApi(context.Request.Path))
        {
            await next(context);
            return;
        }

        var startTimestamp = timeProvider.GetTimestamp();
        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            metrics.RecordRequestDuration(
                $"{context.Request.Method} {ResolveRouteTemplate(context)}",
                statusCode,
                timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Mẫu route của endpoint đã khớp. Exception handler xóa endpoint của request trước khi dựng response lỗi và giữ bản gốc trong
    /// <see cref="IExceptionHandlerFeature"/>, nên đọc cả hai nguồn để 409/503/504 vẫn mang mẫu route thật.
    /// </summary>
    private static string ResolveRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
        var rawText = (endpoint as RouteEndpoint)?.RoutePattern.RawText;
        return rawText is null ? UnmatchedTemplate : RouteConstraintPattern().Replace(rawText, "}");
    }

    /// <summary>Ràng buộc trong tham số route (<c>{decisionId:guid}</c> → <c>{decisionId}</c>) để nhãn khớp đường dẫn trong OpenAPI.</summary>
    [GeneratedRegex(":[^}]*}")]
    private static partial Regex RouteConstraintPattern();
}
