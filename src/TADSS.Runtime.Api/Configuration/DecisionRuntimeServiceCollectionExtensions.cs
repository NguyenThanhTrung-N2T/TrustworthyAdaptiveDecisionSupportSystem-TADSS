using Shared.DecisionContractSDK.Domain;
using TADSS.Application;
using TADSS.Infrastructure;
using TADSS.Runtime.Api.Services.Strategies;

namespace TADSS.Runtime.Api.Configuration;

/// <summary>
/// Nơi duy nhất đăng ký DI cho lõi runtime (contract v2). Mỗi nhóm thành phần bổ sung đăng ký của mình tại đây.
/// </summary>
public static class DecisionRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký phần Application (qua <c>AddApplication</c>), phần Infrastructure (qua <c>AddInfrastructure</c>) và strategy của runtime.
    /// </summary>
    public static IServiceCollection AddDecisionRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        // Phần use case (options, policy, composer, registry, service) do tầng Application đăng ký
        services.AddApplication(configuration);

        // Adapter hạ tầng (DbContext, health check, resilience, cache, kho audit) do tầng Infrastructure đăng ký
        services.AddInfrastructure(configuration);

        // Strategy v2 dựng từ Tadss:Strategies:* + lịch cao điểm; lỗi cấu hình làm registry không dựng được
        // (Program.cs resolve sớm nên ứng dụng không khởi động)
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));

        return services;
    }
}
