using Microsoft.Net.Http.Headers;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Gắn header bảo mật vào mọi response (SEC-3): <c>X-Content-Type-Options: nosniff</c>, <c>X-Frame-Options: DENY</c>,
/// <c>Referrer-Policy: no-referrer</c>, và <c>Cache-Control: no-store</c> cho <c>/api/*</c> (response chứa quyết định/mức cọc không được cache).
/// Đăng ký qua <see cref="HttpResponse.OnStarting(Func{Task})"/> nên header có cả trên response do exception handler ghi:
/// callback <c>OnStarting</c> vẫn còn sau <c>Response.Clear()</c> của exception handler, còn header đặt trực tiếp thì bị xóa.
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string NoSniff = "nosniff";
    private const string FrameDeny = "DENY";
    private const string ReferrerNone = "no-referrer";
    private const string NoStore = "no-store";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            var headers = httpContext.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = NoSniff;
            headers[HeaderNames.XFrameOptions] = FrameDeny;
            headers["Referrer-Policy"] = ReferrerNone;
            if (SecurityPaths.IsApi(httpContext.Request.Path))
            {
                headers.CacheControl = NoStore;
            }

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
