namespace TADSS.Runtime.Api.Security;

/// <summary>
/// Phân loại đường dẫn cho các bước bảo mật (xác thực API key, về sau là rate limit...).
/// </summary>
public static class SecurityPaths
{
    private static readonly PathString ApiPrefix = new("/api");
    private static readonly PathString HealthLive = new("/health/live");
    private static readonly PathString HealthReady = new("/health/ready");
    private static readonly PathString SwaggerPrefix = new("/swagger");

    /// <summary>Đường dẫn thuộc bề mặt API được bảo vệ bằng API key (<c>/api/*</c>).</summary>
    public static bool IsApi(PathString path) => path.StartsWithSegments(ApiPrefix);

    /// <summary>Endpoint health check (<c>/health/live</c>, <c>/health/ready</c>): miễn xác thực ở mọi môi trường để healthcheck của container gọi được.</summary>
    public static bool IsHealth(PathString path) =>
        path.StartsWithSegments(HealthLive) || path.StartsWithSegments(HealthReady);

    /// <summary>Swagger UI và tài liệu OpenAPI (<c>/swagger/*</c>); chỉ được miễn xác thực ở môi trường Development.</summary>
    public static bool IsSwagger(PathString path) => path.StartsWithSegments(SwaggerPrefix);
}
