using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TADSS.Application.Common;
using TADSS.Application.Decisions;
using TADSS.Application.Governance;
using TADSS.Application.Options;
using TADSS.Application.Outcomes;
using TADSS.Application.Routing;
using TADSS.Application.Shadow;
using TADSS.Application.Strategies;

namespace TADSS.Application;

/// <summary>
/// Đăng ký DI cho tầng Application: options của use case (có kiểm tra khi khởi động), lịch cao điểm, policy,
/// bộ ghép response, bộ đánh giá strategy, registry và router strategy, shadow và các use case (đánh giá, tra cứu, override, outcome). Hạ tầng (kho audit, cache, pipeline) do project Infrastructure đăng ký.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký các thành phần của Application. Registry strategy cần các <c>RegisteredStrategy</c> do composition root cung cấp.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Meter TADSS.Runtime: nơi duy nhất khai báo tên metric; singleton, dispose cùng host
        services.TryAddSingleton<TadssMetrics>();

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

        // Danh bạ strategy (tra theo tên) và định tuyến theo request: section Tadss:Routing (A-01). Cấu hình sai lúc khởi động thì không khởi động;
        // nạp lại nóng do StrategyRouter theo dõi qua port IRoutingRulesSource (A-02; adapter do AddInfrastructure đăng ký), không qua IOptionsMonitor
        services.AddSingleton<IStrategyRegistry, StrategyRegistry>();
        services.AddOptions<StrategyRoutingOptions>()
            .Bind(configuration.GetSection(StrategyRoutingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StrategyRoutingOptions>, StrategyRoutingOptionsValidator>();
        services.AddSingleton<IStrategyRouter, StrategyRouter>();

        // Shadow (A-11, section Tadss:Shadow, mặc định tắt): hàng đợi có giới hạn (singleton) và bộ thực thi mỗi việc (scoped vì dùng kho audit scoped);
        // worker nền đọc hàng đợi do AddInfrastructure đăng ký. Bật mà strategy shadow không có trong registry thì không khởi động
        services.AddOptions<ShadowOptions>()
            .Bind(configuration.GetSection(ShadowOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ShadowOptions>, ShadowOptionsValidator>();
        services.AddSingleton<ShadowDispatcher>();
        services.AddSingleton<IShadowDispatcher>(sp => sp.GetRequiredService<ShadowDispatcher>());
        services.AddScoped<ShadowExecutor>();

        // Bộ ghép response + bản ghi audit (phép biến đổi thuần)
        services.AddSingleton<DecisionResponseComposer>();

        // Đánh giá strategy (cache + pipeline): chỉ phụ thuộc pipeline, cache, đồng hồ và logger (đều singleton)
        services.AddSingleton<StrategyEvaluator>();

        // Điều phối đánh giá quyết định: scoped vì phụ thuộc kho audit scoped (strategy/evaluator vẫn singleton)
        services.AddScoped<IDecisionService, DecisionService>();

        // Tra cứu chi tiết quyết định chỉ đọc qua kho audit nên cùng vòng đời scoped với kho
        services.AddScoped<IDecisionQueryService, DecisionQueryService>();

        // Override và outcome ghi qua kho quản trị scoped nên cùng vòng đời scoped
        services.AddScoped<IOverrideService, OverrideService>();
        services.AddScoped<IOutcomeService, OutcomeService>();

        return services;
    }
}
