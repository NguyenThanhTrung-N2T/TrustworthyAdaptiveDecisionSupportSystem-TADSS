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
    /// Dựng pipeline: forwarded headers (khi bật) → correlation id → exception handler → security headers + HSTS → request logging → https redirection →
    /// giới hạn kích thước → Swagger (chỉ Development) → xác thực API key → routing (rate limiter thêm trước routing ở Task 7).
    /// Logging đứng trước các bước từ chối nên ghi được cả 401/413; kích thước kiểm tra trước xác thực; Swagger đứng trước xác thực nên được miễn ở Development;
    /// routing gọi tường minh để định tuyến chạy sau các bước bảo mật. Endpoint (controller, health check) do <c>Program.cs</c> map sau lời gọi này.
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

        // 3. Pipeline xử lý lỗi RFC 7807 ProblemDetails
        app.UseExceptionHandler();

        // 4. Header bảo mật (có cả trên response của exception handler) và HSTS ngoài Development/Testing
        app.UseMiddleware<SecurityHeadersMiddleware>();
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
        }

        // 5. Request logging: đứng trước các bước từ chối để ghi cả 401/413
        app.UseSerilogRequestLogging();

        // 6. https redirection
        app.UseHttpsRedirection();

        // 7. Giới hạn kích thước body: trước xác thực nên request quá lớn không cần khóa vẫn nhận 413
        app.UseMiddleware<RequestSizeLimitMiddleware>();

        // 8. Swagger chỉ ở Development, trước xác thực để được miễn
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v2/swagger.json", "TADSS Decision Runtime API v2"));
        }

        // 9. Xác thực API key cho /api/*
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        // 10. Rate limiter: Task 7 thêm tại đây

        // 11. Routing tường minh: định tuyến chạy sau các bước bảo mật (WebApplication mặc định chèn routing ở đầu pipeline)
        app.UseRouting();

        return app;
    }
}
