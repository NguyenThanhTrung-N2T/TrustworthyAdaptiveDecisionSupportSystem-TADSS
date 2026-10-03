using Serilog;
using TADSS.Runtime.Api.Middlewares;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Thứ tự middleware của request pipeline, khai báo tại một nơi duy nhất (E-12).
/// </summary>
public static class RequestPipelineExtensions
{
    /// <summary>
    /// Dựng pipeline: correlation id → exception handler → Swagger (chỉ Development) → request logging → https redirection → authorization.
    /// Endpoint (controller, health check) do <c>Program.cs</c> map sau lời gọi này.
    /// </summary>
    public static WebApplication UseTadssRequestPipeline(this WebApplication app)
    {
        // 1. Correlation ID Middleware (Canonical Correlation Propagation)
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 2. Pipeline xử lý lỗi RFC 7807 ProblemDetails
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v2/swagger.json", "TADSS Decision Runtime API v2"));
        }

        app.UseSerilogRequestLogging();

        app.UseHttpsRedirection();
        app.UseAuthorization();

        return app;
    }
}
