using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.Options;
using TADSS.Infrastructure.Persistence;
using TADSS.Runtime.Api.Options;

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

        // API key rỗng ở Development/Testing: vẫn khởi động (validator cho phép) nhưng mọi request /api/* sẽ bị 401, nên cảnh báo rõ
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.ApiKeys.Count == 0)
        {
            app.Logger.LogWarning(
                "{Section}:ApiKeys đang rỗng: mọi request /api/* sẽ bị 401. Cấp khóa qua biến môi trường Tadss__Security__ApiKeys__0__ClientId/KeySha256 hoặc file cấu hình dev.",
                SecurityOptions.SectionName);
        }

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
