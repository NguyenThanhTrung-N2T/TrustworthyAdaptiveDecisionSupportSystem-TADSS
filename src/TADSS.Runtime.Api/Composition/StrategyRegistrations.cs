using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Strategies;
using TADSS.Strategies.Default;
using TADSS.Strategies.MockAlternative;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Dựng các strategy từ cấu hình (spec 4.2.2): bind tham số từ <c>Tadss:Strategies:{Default|MockAlternative}</c>,
/// kiểm tra bằng <c>Validate()</c> của options và tính <c>optionsFingerprint</c> (gồm cả lịch cao điểm, B-08).
/// Thiếu section thì dùng giá trị mặc định của options. Tham số sai ném <see cref="InvalidOperationException"/> nêu tên
/// section nên ứng dụng không khởi động. Tham số đổi phải khởi động lại.
/// </summary>
public static class StrategyRegistrations
{
    /// <summary>
    /// Section cấu hình của <see cref="DefaultDecisionStrategy"/>.
    /// </summary>
    public const string DefaultSectionName = "Tadss:Strategies:Default";

    /// <summary>
    /// Section cấu hình của <see cref="MockAlternativeDecisionStrategy"/>.
    /// </summary>
    public const string MockAlternativeSectionName = "Tadss:Strategies:MockAlternative";

    /// <summary>
    /// Dựng <see cref="DefaultDecisionStrategy"/> cùng fingerprint.
    /// </summary>
    /// <param name="configuration">Cấu hình ứng dụng.</param>
    /// <param name="calendar">Lịch cao điểm đưa cho mọi strategy.</param>
    /// <exception cref="InvalidOperationException">Section <see cref="DefaultSectionName"/> không bind được hoặc không hợp lệ.</exception>
    public static RegisteredStrategy CreateDefault(IConfiguration configuration, BranchCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(calendar);

        var options = Bind<DefaultStrategyOptions>(configuration, DefaultSectionName, o => o.Validate());

        // Danh sách tham số và thứ tự khai báo tường minh: thêm/bớt tham số của strategy phải sửa ở đây
        var fingerprint = new OptionsFingerprintBuilder();
        fingerprint.Add(nameof(options.HighValueThreshold), options.HighValueThreshold);
        fingerprint.Add(nameof(options.NoShowReviewThreshold), options.NoShowReviewThreshold);
        fingerprint.Add(nameof(options.PaymentExpiryRejectThreshold), options.PaymentExpiryRejectThreshold);
        fingerprint.Add(nameof(options.StandardDepositRate), options.StandardDepositRate);
        fingerprint.Add(nameof(options.SilverDepositRate), options.SilverDepositRate);
        fingerprint.Add(nameof(options.GoldDepositRate), options.GoldDepositRate);
        fingerprint.Add(nameof(options.PremiumDepositRate), options.PremiumDepositRate);
        fingerprint.Add(nameof(options.CacheTtlMinutes), options.CacheTtlMinutes);
        fingerprint.Add(nameof(options.OffPeakDiscountEnabled), options.OffPeakDiscountEnabled ? "true" : "false");
        fingerprint.Add(nameof(options.OffPeakDiscountRate), options.OffPeakDiscountRate);
        // Thứ tự khai báo hạng không đổi hành vi nên không được đổi fingerprint
        fingerprint.Add(nameof(options.OffPeakDiscountTiers), string.Join(',', options.OffPeakDiscountTiers.Order()));
        fingerprint.Add(nameof(options.OffPeakDiscountMaxNoShowCount), options.OffPeakDiscountMaxNoShowCount);
        fingerprint.Add(nameof(options.OffPeakDiscountMaxPaymentExpiredCount), options.OffPeakDiscountMaxPaymentExpiredCount);
        fingerprint.AddCalendar(calendar);

        // Cùng một fingerprint cho khóa cache và cho lý do của quyết định được giảm cọc (truy vết cấu hình đã dùng)
        var optionsFingerprint = fingerprint.Build();
        return new RegisteredStrategy(new DefaultDecisionStrategy(options, optionsFingerprint), optionsFingerprint);
    }

    /// <summary>
    /// Dựng <see cref="MockAlternativeDecisionStrategy"/> cùng fingerprint.
    /// </summary>
    /// <param name="configuration">Cấu hình ứng dụng.</param>
    /// <param name="calendar">Lịch cao điểm đưa cho mọi strategy.</param>
    /// <exception cref="InvalidOperationException">Section <see cref="MockAlternativeSectionName"/> không bind được hoặc không hợp lệ.</exception>
    public static RegisteredStrategy CreateMockAlternative(IConfiguration configuration, BranchCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(calendar);

        var options = Bind<MockAlternativeStrategyOptions>(configuration, MockAlternativeSectionName, o => o.Validate());

        var fingerprint = new OptionsFingerprintBuilder();
        fingerprint.Add(nameof(options.HighValueThreshold), options.HighValueThreshold);
        fingerprint.Add(nameof(options.PeakDepositRate), options.PeakDepositRate);
        fingerprint.Add(nameof(options.OffPeakDepositRate), options.OffPeakDepositRate);
        fingerprint.Add(nameof(options.CacheTtlMinutes), options.CacheTtlMinutes);
        fingerprint.AddCalendar(calendar);

        return new RegisteredStrategy(new MockAlternativeDecisionStrategy(options), fingerprint.Build());
    }

    /// <summary>
    /// Bind một section vào options (thiếu section thì dùng mặc định của POCO) rồi kiểm tra; mọi lỗi được bọc lại kèm
    /// tên section. Binder của .NET đọc số theo culture bất biến. Khóa lạ (gõ sai tên tham số) là lỗi, không bị bỏ qua âm thầm:
    /// gõ sai tên khóa bật một chính sách mà ứng dụng vẫn chạy với chính sách tắt thì người vận hành không biết cấu hình chưa có hiệu lực.
    /// </summary>
    private static TOptions Bind<TOptions>(IConfiguration configuration, string sectionName, Action<TOptions> validate)
        where TOptions : class, new()
    {
        try
        {
            var options = configuration.GetSection(sectionName).Get<TOptions>(binder => binder.ErrorOnUnknownConfiguration = true) ?? new TOptions();
            validate(options);
            return options;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Cấu hình '{sectionName}' không hợp lệ: {ex.Message}", ex);
        }
    }
}
