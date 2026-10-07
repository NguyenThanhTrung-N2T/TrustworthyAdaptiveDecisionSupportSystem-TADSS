using Serilog.AspNetCore;
using Serilog.Events;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Tùy chọn của dòng log hoàn tất request (Serilog request logging): mức log theo status cuối và làm giàu <c>ClientId</c>.
/// Template giữ mặc định của Serilog; không log header nên không bao giờ lộ <c>X-Api-Key</c>.
/// </summary>
public static class RequestLoggingConfiguration
{
    /// <summary>Request lỗi phía server (status ≥ 500) hoặc exception chưa được xử lý ghi mức Error; còn lại Information.</summary>
    public const int ServerErrorStatusThreshold = StatusCodes.Status500InternalServerError;

    /// <summary>Gán mức log và bộ làm giàu cho <paramref name="options"/>.</summary>
    public static void Configure(RequestLoggingOptions options)
    {
        options.GetLevel = (context, _, exception) =>
            exception is not null || context.Response.StatusCode >= ServerErrorStatusThreshold
                ? LogEventLevel.Error
                : LogEventLevel.Information;

        // ClientId do bước xác thực gắn vào HttpContext.Items; LogContext của bước đó đã đóng khi dòng hoàn tất được ghi
        options.EnrichDiagnosticContext = (diagnosticContext, context) =>
        {
            var clientId = ApiClientIdentity.Get(context);
            if (clientId is not null)
            {
                diagnosticContext.Set("ClientId", clientId);
            }
        };
    }
}
