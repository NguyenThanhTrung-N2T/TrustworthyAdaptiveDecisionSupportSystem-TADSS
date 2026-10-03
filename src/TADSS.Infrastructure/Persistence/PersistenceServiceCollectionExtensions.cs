using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.Persistence;

/// <summary>
/// Đăng ký <see cref="TadssDbContext"/> (PostgreSQL) cho kho audit. Giữ phần dùng EF Core trong namespace Persistence,
/// <c>AddInfrastructure</c> chỉ ghép các adapter lại với nhau.
/// </summary>
internal static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Thêm <see cref="TadssDbContext"/> với Npgsql; migration nằm trong assembly Infrastructure (cùng assembly với DbContext),
    /// bảng lịch sử migration ở schema <c>tadss</c>. Chuỗi kết nối đọc từ <see cref="DatabaseOptions"/> lúc tạo DbContext (đã được validator kiểm khi khởi động). Thời gian audit do DecisionService gán, không có interceptor ghi timestamp.
    /// </summary>
    public static IServiceCollection AddPersistenceDbContext(this IServiceCollection services)
    {
        services.AddDbContext<TadssDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(TadssDbContext).Assembly.GetName().Name);
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "tadss");
            });
        });

        return services;
    }
}
