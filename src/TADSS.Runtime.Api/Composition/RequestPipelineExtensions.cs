using Microsoft.Extensions.Options;
using Serilog;
using TADSS.Runtime.Api.Middlewares;
using TADSS.Runtime.Api.Options;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Thứ tự middleware của request pipeline (F-09), khai báo tại một nơi duy nhất.
/// </summary>
public static class RequestPipelineExtensions
{
    /// <summary>
    /// Dựng pipeline: forwarded headers (khi bật) → correlation id → request logging → request metrics → exception handler → security headers + HSTS → https redirection →
    /// giới hạn kích thước → Swagger (chỉ Development) → xác thực API key → rate limiter → routing.
    /// Logging và metrics đứng <b>ngoài</b> exception handler nên thấy status cuối (409/503/504 do handler dựng không bị ghi thành 500) và ghi được cả 401/413/429;
    /// rate limiter đứng sau xác thực nên request thiếu khóa nhận 401 chứ không tốn lượt giới hạn; kích thước kiểm tra trước xác thực; Swagger đứng trước xác thực nên được miễn ở Development;
    /// routing gọi tường minh để định tuyến chạy sau các bước bảo mật. Endpoint (controller, health check) do <see cref="EndpointMappingExtensions.MapTadssEndpoints"/> map sau lời gọi này.
    /// Không có <c>UseAuthorization</c>: không có policy nào, xác thực do <see cref="ApiKeyAuthenticationMiddleware"/> đảm nhiệm.
    /// </summary>
    public static WebApplication UseTadssRequestPipeline(this WebApplication app)
    {
        // 1. Forwarded headers: chỉ khi tin proxy (Tadss:Security:TrustForwardedHeaders), để Request.IsHttps/Host/IP phản ánh client thật
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.TrustForwardedHeaders)
        {
            app.UseForwardedHeaders();
        }

        // 2. Correlation ID Middleware (Canonical Correlation Propagation)
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 3. Request logging: ngoài exception handler để ghi status cuối và ghi cả 401/413/429; logger lấy từ DI (cùng đối tượng với logger của host)
        app.UseSerilogRequestLogging(options =>
        {
            RequestLoggingConfiguration.Configure(options);
            options.Logger = app.Services.GetService<Serilog.ILogger>();
        });

        // 4. Request metrics (thời gian request /api/*): cũng ngoài exception handler
        app.UseMiddleware<RequestMetricsMiddleware>();

        // 5. Pipeline xử lý lỗi RFC 7807 ProblemDetails
        app.UseExceptionHandler();

        // 6. Header bảo mật và HSTS (ngoài Development/Testing, chỉ https): gắn qua OnStarting nên có cả trên response của exception handler
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // 7. https redirection
        app.UseHttpsRedirection();

        // 8. Giới hạn kích thước body: trước xác thực nên request quá lớn không cần khóa vẫn nhận 413
        app.UseMiddleware<RequestSizeLimitMiddleware>();

        // 9. Swagger chỉ ở Development, trước xác thực để được miễn
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v2/swagger.json", "TADSS Decision Runtime API v2"));
        }

        // 10. Xác thực API key cho /api/*
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        // 11. Rate limiter theo client (ApiClientIdentity do bước 10 gắn); /health/* không bị giới hạn
        app.UseRateLimiter();

        // 12. Routing tường minh: định tuyến chạy sau các bước bảo mật (WebApplication mặc định chèn routing ở đầu pipeline)
        app.UseRouting();

        return app;
    }
}
