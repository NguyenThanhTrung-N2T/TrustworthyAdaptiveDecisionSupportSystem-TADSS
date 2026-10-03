using Shared.DecisionContractSDK.Domain;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký strategy cụ thể của runtime. Chọn strategy nào là việc lắp ráp của composition root (E-03):
/// Application chỉ biết <c>IStrategyRegistry</c> và <c>RegisteredStrategy</c>, không tham chiếu project strategy.
/// </summary>
public static class StrategyServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký hai <c>RegisteredStrategy</c> (Default, MockAlternative) dựng từ <c>Tadss:Strategies:*</c> và lịch cao điểm.
    /// Lỗi cấu hình làm registry không dựng được; <see cref="StartupTasks"/> resolve registry sớm nên ứng dụng không khởi động.
    /// </summary>
    public static IServiceCollection AddStrategies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));

        return services;
    }
}
