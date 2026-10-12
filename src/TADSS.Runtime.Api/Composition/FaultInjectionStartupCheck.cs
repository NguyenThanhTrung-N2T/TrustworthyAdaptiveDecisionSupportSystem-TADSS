using TADSS.Infrastructure.Options;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Cảnh báo khi khởi động về cấu hình tiêm lỗi strategy (A-07). Không dừng khởi động: <c>Enabled = true</c> ngoài <c>Testing</c> chỉ bị bỏ qua
/// (decorator không được bọc), tên <c>TargetStrategy</c> lạ chỉ làm decorator không áp cho strategy nào.
/// </summary>
public static class FaultInjectionStartupCheck
{
    /// <summary>
    /// Ghi Warning một lần nếu <paramref name="options"/> bật tiêm lỗi: ngoài môi trường <c>Testing</c> (bị bỏ qua), hoặc ở <c>Testing</c>
    /// (đang tiêm lỗi; kèm cảnh báo riêng khi <c>TargetStrategy</c> không có trong <paramref name="registeredStrategyNames"/>). Tắt thì im lặng.
    /// </summary>
    public static void Log(
        ILogger logger,
        IHostEnvironment environment,
        FaultInjectionOptions options,
        IReadOnlyCollection<string> registeredStrategyNames)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registeredStrategyNames);

        if (!options.Enabled)
        {
            return;
        }

        if (!environment.IsEnvironment(StrategyServiceCollectionExtensions.FaultInjectionEnvironment))
        {
            logger.LogWarning(
                "{Section}:Enabled đang bật ở môi trường {Environment} nhưng bị bỏ qua: chỉ môi trường {AllowedEnvironment} mới bọc decorator tiêm lỗi. Tắt cờ này ngoài môi trường thử nghiệm.",
                FaultInjectionOptions.SectionName,
                environment.EnvironmentName,
                StrategyServiceCollectionExtensions.FaultInjectionEnvironment);
            return;
        }

        logger.LogWarning(
            "Tiêm lỗi strategy đang BẬT (profile {Profile}, DelayMs {DelayMs}, mục tiêu {Target}); chỉ dùng cho kiểm thử độ chịu lỗi.",
            options.Profile,
            options.DelayMs,
            string.IsNullOrEmpty(options.TargetStrategy) ? "mọi strategy" : options.TargetStrategy);

        if (!registeredStrategyNames.Any(options.Targets))
        {
            logger.LogWarning(
                "{Section}:TargetStrategy '{Target}' không khớp strategy nào trong registry nên không strategy nào bị tiêm lỗi. Các strategy hợp lệ: [{Available}].",
                FaultInjectionOptions.SectionName,
                options.TargetStrategy,
                string.Join(", ", registeredStrategyNames));
        }
    }
}
