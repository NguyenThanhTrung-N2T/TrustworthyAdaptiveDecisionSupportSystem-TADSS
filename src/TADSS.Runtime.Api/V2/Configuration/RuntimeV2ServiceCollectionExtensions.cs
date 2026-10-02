using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.V2.Domain;
using TADSS.Runtime.Api.Configuration;
using TADSS.Runtime.Api.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Infrastructure.Resilience;
using TADSS.Runtime.Api.V2.Services.Decisions;
using TADSS.Runtime.Api.V2.Services.Strategies;

namespace TADSS.Runtime.Api.V2.Configuration;

/// <summary>
/// Nơi duy nhất đăng ký DI cho runtime v2. Mỗi nhóm thành phần của gói 5.2b bổ sung đăng ký của mình tại đây.
/// </summary>
public static class RuntimeV2ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký cấu hình (có kiểm tra khi khởi động), lịch cao điểm và các dịch vụ nền của runtime v2.
    /// </summary>
    public static IServiceCollection AddDecisionRuntimeV2(this IServiceCollection services, IConfiguration configuration)
    {
        // Thời gian lấy qua TimeProvider để test điều khiển được đồng hồ
        services.TryAddSingleton(TimeProvider.System);

        // Options + validator fail-fast: cấu hình sai thì ứng dụng không khởi động
        services.AddOptions<ApprovalOptions>()
            .Bind(configuration.GetSection(ApprovalOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ApprovalOptions>, ApprovalOptionsValidator>();

        services.AddOptions<ValidationOptions>()
            .Bind(configuration.GetSection(ValidationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ValidationOptions>, ValidationOptionsValidator>();

        services.AddOptions<CalendarOptions>()
            .Bind(configuration.GetSection(CalendarOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CalendarOptions>, CalendarOptionsValidator>();

        // Lịch cao điểm dùng chung cho router và mọi strategy (nguồn duy nhất của "giờ cao điểm")
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<CalendarOptions>>().Value.ToCalendar());

        services.AddSingleton<ItemDurationPolicy>();
        services.AddSingleton<DepositCalculator>();
        services.AddSingleton<ApprovalWindowPolicy>();

        // Strategy đang kích hoạt dùng chung section Tadss:Strategy với v1 (bind lại cùng section là vô hại)
        services.AddOptions<StrategyOptions>().Bind(configuration.GetSection(StrategyOptions.SectionName));

        // Strategy v2 dựng từ Tadss:Strategies:* + lịch cao điểm; lỗi cấu hình làm registry không dựng được
        // (Program.cs resolve sớm nên ứng dụng không khởi động)
        services.AddSingleton(sp => StrategyRegistrations.CreateDefault(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton(sp => StrategyRegistrations.CreateMockAlternative(configuration, sp.GetRequiredService<BranchCalendar>()));
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();

        // Cấu hình chịu lỗi dùng chung section Tadss:Resilience với v1: kiểm tra ngân sách thời gian khi khởi động
        // (áp dụng cho cả v1). Pipeline v2 là singleton riêng, không chia sẻ circuit breaker với v1.
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
        services.AddSingleton<IStrategyPipeline, StrategyPipeline>();

        return services;
    }
}
