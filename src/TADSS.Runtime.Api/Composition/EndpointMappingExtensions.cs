using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Map endpoint của Runtime.Api tại một nơi: controller v2 và hai health endpoint (SEC-5). Gọi sau <see cref="RequestPipelineExtensions.UseTadssRequestPipeline"/>.
/// </summary>
public static class EndpointMappingExtensions
{
    /// <summary>Tag của check thuộc readiness; trùng tag mà Infrastructure gắn cho check PostgreSQL và Redis.</summary>
    private const string ReadyTag = "ready";

    /// <summary>
    /// Map controller và health: <c>/health/live</c> không chạy check nào (chỉ cho biết process còn sống); <c>/health/ready</c> chạy các check tag <c>ready</c>
    /// (<c>Unhealthy</c> → 503, <c>Degraded</c> → 200). Body giữ writer mặc định (chỉ chữ trạng thái) nên không lộ chi tiết hạ tầng.
    /// Không có endpoint <c>/health</c> gộp (F-16): nó trùng <c>/health/ready</c> và làm rộng bề mặt tấn công.
    /// </summary>
    public static WebApplication MapTadssEndpoints(this WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) });
        return app;
    }
}
