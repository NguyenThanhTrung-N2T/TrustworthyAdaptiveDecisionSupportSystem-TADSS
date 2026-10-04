using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Middlewares;

/// <summary>
/// Gắn header bảo mật vào mọi response (SEC-3): <c>X-Content-Type-Options: nosniff</c>, <c>X-Frame-Options: DENY</c>,
/// <c>Referrer-Policy: no-referrer</c>, <c>Cache-Control: no-store</c> cho <c>/api/*</c> (response chứa quyết định/mức cọc không được cache),
/// và <c>Strict-Transport-Security</c> (HSTS) khi chạy ngoài <c>Development</c>/<c>Testing</c> với request https tới host không nằm trong
/// <see cref="HstsOptions.ExcludedHosts"/> (cùng quy tắc của <c>UseHsts</c>, nên không dùng <c>UseHsts</c>: middleware đó đặt header trực tiếp và bị xóa khi lỗi).
/// Đăng ký qua <see cref="HttpResponse.OnStarting(Func{Task})"/> nên header có cả trên response do exception handler ghi:
/// callback <c>OnStarting</c> vẫn còn sau <c>Response.Clear()</c> của exception handler, còn header đặt trực tiếp thì bị xóa.
/// </summary>
public class SecurityHeadersMiddleware
{
    private const string NoSniff = "nosniff";
    private const string FrameDeny = "DENY";
    private const string ReferrerNone = "no-referrer";
    private const string NoStore = "no-store";

    private readonly RequestDelegate _next;
    private readonly string? _hstsValue;
    private readonly string[] _hstsExcludedHosts;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<HstsOptions> hstsOptions, IHostEnvironment environment)
    {
        _next = next;
        var hsts = hstsOptions.Value;
        var hstsEnabled = !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
        _hstsExcludedHosts = [.. hsts.ExcludedHosts];
        _hstsValue = hstsEnabled
            ? $"max-age={(long)hsts.MaxAge.TotalSeconds}{(hsts.IncludeSubDomains ? "; includeSubDomains" : string.Empty)}{(hsts.Preload ? "; preload" : string.Empty)}"
            : null;
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(state =>
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

            if (_hstsValue is not null
                && httpContext.Request.IsHttps
                && !_hstsExcludedHosts.Contains(httpContext.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
            {
                headers.StrictTransportSecurity = _hstsValue;
            }

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }
}
