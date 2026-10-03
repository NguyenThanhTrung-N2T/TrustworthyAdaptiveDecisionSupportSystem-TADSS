using Microsoft.Extensions.DependencyInjection;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Đăng ký health check PostgreSQL cho kho audit (nhóm <c>ready</c>/<c>db</c>, dùng cho endpoint <c>/health/ready</c>).
/// </summary>
internal static class PersistenceHealthChecks
{
    /// <summary>Tên health check PostgreSQL.</summary>
    public const string Name = "postgresql";

    /// <summary>
    /// Thêm health check PostgreSQL với tag <c>ready</c> và <c>db</c>.
    /// </summary>
    public static IServiceCollection AddPersistenceHealthChecks(this IServiceCollection services, string? connectionString)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                connectionString: connectionString ?? string.Empty,
                name: Name,
                tags: new[] { "ready", "db" });

        return services;
    }
}
