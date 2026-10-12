using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Strategies;
using TADSS.Infrastructure.FaultInjection;
using TADSS.Infrastructure.Options;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký strategy cụ thể của runtime. Chọn strategy nào là việc lắp ráp của composition root:
/// Application chỉ biết <c>IStrategyRegistry</c> và <c>RegisteredStrategy</c>, không tham chiếu project strategy.
/// </summary>
public static class StrategyServiceCollectionExtensions
{
    /// <summary>Tên môi trường duy nhất được phép bọc decorator tiêm lỗi (A-07).</summary>
    public const string FaultInjectionEnvironment = "Testing";

    /// <summary>
    /// Đăng ký hai <c>RegisteredStrategy</c> (Default, MockAlternative) dựng từ <c>Tadss:Strategies:*</c> và lịch cao điểm.
    /// Lỗi cấu hình làm registry không dựng được; <see cref="StartupTasks"/> resolve registry sớm nên ứng dụng không khởi động.
    /// Khi <paramref name="environment"/> là <see cref="FaultInjectionEnvironment"/> và <c>Tadss:Testing:FaultInjection:Enabled = true</c>
    /// thì strategy khớp <c>TargetStrategy</c> (hoặc mọi strategy khi để trống) được bọc <see cref="FaultInjectionStrategyDecorator"/>;
    /// ở môi trường khác không bao giờ bọc. Cờ <c>Enabled</c> và <c>TargetStrategy</c> đọc từ <see cref="IOptions{TOptions}"/> lúc resolve (cấu hình cuối cùng, gồm override của host test)
    /// và không đổi nóng; chỉ <c>Profile</c>/<c>DelayMs</c> đổi nóng qua <see cref="FaultInjectionControl"/>.
    /// </summary>
    public static IServiceCollection AddStrategies(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var faultInjectionAllowed = environment.IsEnvironment(FaultInjectionEnvironment);

        // Giữ cấu hình tiêm lỗi hiện hành (đổi nóng Profile/DelayMs); singleton dựng lười, chỉ được resolve khi decorator thật sự được bọc
        services.AddSingleton<FaultInjectionControl>();

        services.AddSingleton(sp => WithFaultInjection(
            sp,
            faultInjectionAllowed,
            StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>())));
        services.AddSingleton(sp => WithFaultInjection(
            sp,
            faultInjectionAllowed,
            StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>())));

        return services;
    }

    /// <summary>
    /// Bọc <paramref name="registered"/> bằng decorator tiêm lỗi khi môi trường cho phép, <c>Enabled = true</c> và tên khớp <c>TargetStrategy</c>.
    /// <c>OptionsFingerprint</c> giữ nguyên để khóa cache và audit không đổi.
    /// </summary>
    private static RegisteredStrategy WithFaultInjection(IServiceProvider sp, bool environmentAllowed, RegisteredStrategy registered)
    {
        if (!environmentAllowed)
        {
            return registered;
        }

        var options = sp.GetRequiredService<IOptions<FaultInjectionOptions>>().Value;
        if (!options.Enabled || !options.Targets(registered.Strategy.StrategyName))
        {
            return registered;
        }

        var decorator = new FaultInjectionStrategyDecorator(
            registered.Strategy, sp.GetRequiredService<FaultInjectionControl>(), sp.GetRequiredService<TimeProvider>());
        return registered with { Strategy = decorator };
    }
}
