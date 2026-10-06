using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TADSS.Application.Options;
using TADSS.Application.Routing;
using TADSS.Application.Shadow;
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
    /// <summary>Khóa cấu hình cũ đã gỡ (A-03), chỉ còn để cảnh báo khi cấu hình cuối cùng vẫn đặt nó.</summary>
    private const string LegacyActiveStrategyKey = "Tadss:Strategy:ActiveStrategyName";

    /// <summary>
    /// Kiểm tra sớm registry, router strategy và shadow, cảnh báo khóa cấu hình cũ và (khi bật) tự áp migration.
    /// </summary>
    public static async Task RunAsync(WebApplication app)
    {
        // A-03: khóa cũ bị bỏ qua, chỉ cảnh báo (không dừng khởi động, không in giá trị)
        if (!string.IsNullOrWhiteSpace(app.Configuration[LegacyActiveStrategyKey]))
        {
            app.Logger.LogWarning(
                "Khóa cấu hình {LegacyKey} đã được thay bằng {ReplacementKey} và bị bỏ qua; xóa khóa cũ khỏi cấu hình.",
                LegacyActiveStrategyKey,
                $"{StrategyRoutingOptions.SectionName}:{nameof(StrategyRoutingOptions.DefaultStrategy)}");
        }

        // Eager startup validation: tham số strategy sai thì registry không dựng được; Tadss:Routing sai (validator, tên strategy lạ) thì router
        // không dựng được → ứng dụng không khởi động
        var registry = app.Services.GetRequiredService<IStrategyRegistry>();
        _ = app.Services.GetRequiredService<IStrategyRouter>();

        // Shadow bật mà Tadss:Shadow:Strategy không có trong registry thì dispatcher không dựng được → không khởi động
        _ = app.Services.GetRequiredService<IShadowDispatcher>();

        // Tiêm lỗi strategy (A-07): cảnh báo khi cờ bật ngoài Testing hoặc nhắm tên strategy lạ; không dừng khởi động
        FaultInjectionStartupCheck.Log(
            app.Logger,
            app.Environment,
            app.Services.GetRequiredService<IOptions<FaultInjectionOptions>>().Value,
            registry.All.Select(registered => registered.Strategy.StrategyName).ToArray());

        // API key rỗng ở Development/Testing: vẫn khởi động (validator cho phép) nhưng mọi request /api/* sẽ bị 401, nên cảnh báo rõ
        if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.ApiKeys.Count == 0)
        {
            app.Logger.LogWarning(
                "{Section}:ApiKeys đang rỗng: mọi request /api/* sẽ bị 401. Cấp khóa qua biến môi trường Tadss__Security__ApiKeys__0__ClientId/KeySha256 hoặc file cấu hình dev.",
                SecurityOptions.SectionName);
        }

        // Tự áp migration khi khởi động — chỉ bật cho database thử dùng một lần (verify-expose.sh, Testcontainers, chế độ đánh giá); Neon/staging giữ mặc định false
        if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ApplyMigrationsOnStartup)
        {
            app.Logger.LogWarning(
                "Tadss:Database:ApplyMigrationsOnStartup đang bật: tự áp migration khi khởi động chỉ dùng cho database thử dùng một lần (Testcontainers, verify-expose.sh, chế độ đánh giá), không bật cho Neon.");
            using var migrationScope = app.Services.CreateScope();
            await migrationScope.ServiceProvider.GetRequiredService<TadssDbContext>().Database.MigrateAsync();
        }
    }
}
