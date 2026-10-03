using Serilog;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Thứ tự middleware của request pipeline, khai báo tại một nơi duy nhất.
/// </summary>
public static class RequestPipelineExtensions
{
    /// <summary>
    /// Dựng pipeline: correlation id → exception handler → request logging → https redirection → Swagger (chỉ Development) →
    /// xác thực API key → routing. Logging đứng trước các bước từ chối nên ghi được cả 401; Swagger đứng trước xác thực nên được miễn ở Development;
    /// routing gọi tường minh để định tuyến chạy sau các bước bảo mật. Endpoint (controller, health check) do <c>Program.cs</c> map sau lời gọi này.
    /// </summary>
    public static WebApplication UseTadssRequestPipeline(this WebApplication app)
    {
        // 1. Correlation ID Middleware (Canonical Correlation Propagation)
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 2. Pipeline xử lý lỗi RFC 7807 ProblemDetails
        app.UseExceptionHandler();

        // 3. Request logging: đứng trước các bước từ chối để ghi cả 401
        app.UseSerilogRequestLogging();

        // 4. https redirection
        app.UseHttpsRedirection();

        // 5. Swagger chỉ ở Development, trước xác thực để được miễn
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v2/swagger.json", "TADSS Decision Runtime API v2"));
        }

        // 6. Xác thực API key cho /api/*
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        // 7. Routing tường minh: định tuyến chạy sau các bước bảo mật (WebApplication mặc định chèn routing ở đầu pipeline)
        app.UseRouting();

        return app;
    }
}
