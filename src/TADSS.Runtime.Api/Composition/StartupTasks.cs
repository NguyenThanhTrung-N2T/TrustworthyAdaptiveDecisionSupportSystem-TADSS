using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Persistence;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Việc chạy một lần khi khởi động, sau <c>Build()</c> và trước khi nhận request.
/// </summary>
public static class StartupTasks
{
    /// <summary>
    /// Kiểm tra sớm registry strategy và (khi bật) tự áp migration.
    /// </summary>
    public static async Task RunAsync(WebApplication app)
    {
        // Eager startup validation cho registry strategy: tham số strategy/ActiveStrategyName sai thì không khởi động
        app.Services.GetRequiredService<IStrategyRegistry>();

        // Tự áp migration khi khởi động — chỉ bật ở E-LOCAL (docker compose); Neon/staging giữ mặc định false
        if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
        {
            app.Logger.LogWarning(
                "Tadss:Database:ApplyMigrationsOnStartup đang bật: tự áp migration khi khởi động chỉ dùng cho E-LOCAL, không bật cho Neon/staging.");
            using var migrationScope = app.Services.CreateScope();
            await migrationScope.ServiceProvider.GetRequiredService<TadssDbContext>().Database.MigrateAsync();
        }
    }
}
