using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Đăng ký health check PostgreSQL cho kho audit (nhóm <c>ready</c>/<c>db</c>, dùng cho endpoint <c>/health/ready</c>).
/// </summary>
internal static class PersistenceHealthChecks
{
    /// <summary>Tên health check PostgreSQL.</summary>
    public const string Name = "postgresql";

    /// <summary>
    /// Thêm health check PostgreSQL với tag <c>ready</c> và <c>db</c>. Chuỗi kết nối đọc từ <see cref="DatabaseOptions"/> khi health check chạy, không chụp lúc đăng ký.
    /// </summary>
    public static IServiceCollection AddPersistenceHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                connectionStringFactory: serviceProvider =>
                    serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString ?? string.Empty,
                name: Name,
                tags: new[] { "ready", "db" });

        return services;
    }
}
