namespace TADSS.Infrastructure;

/// <summary>
/// Tag của health check tầng Infrastructure; endpoint <c>/health/ready</c> chỉ chạy các check mang tag <see cref="Ready"/>.
/// </summary>
public static class HealthCheckTags
{
    /// <summary>Check của readiness: <c>/health/ready</c> chạy các check này (PostgreSQL bắt buộc, Redis chỉ làm Degraded).</summary>
    public const string Ready = "ready";
}
