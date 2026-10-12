using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;

namespace TADSS.Infrastructure.FaultInjection;

/// <summary>
/// Giữ cấu hình tiêm lỗi hiện hành và đổi <b>nóng</b> <c>Profile</c>/<c>DelayMs</c> khi cấu hình đổi (O-17, chỉ môi trường <c>Testing</c> mới dựng decorator dùng nó),
/// để chế độ đánh giá bật/tắt lỗi giữa một lượt tải mà không khởi động lại. Theo dõi <see cref="IConfiguration"/> như A-02: nạp lại sai (không bind được hoặc không qua validator)
/// (kể cả khi section biến mất do file cụt hoặc bị xóa) → giữ bản cũ, log Error, đếm <c>tadss.config.reload_failures{section=Tadss:Testing:FaultInjection}</c>; callback không bao giờ ném.
/// <para>
/// Chỉ <c>Profile</c> và <c>DelayMs</c> đổi nóng. <c>Enabled</c> và <c>TargetStrategy</c> quyết định việc bọc decorator lúc khởi động nên đổi lúc chạy chỉ được log Information
/// ("chỉ có hiệu lực sau khi khởi động lại"). Profile đổi → một dòng Warning để dòng thời gian log khớp nhật ký sự cố của evaluator. Singleton.
/// </para>
/// </summary>
public sealed class FaultInjectionControl : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<IValidateOptions<FaultInjectionOptions>> _validators;
    private readonly TadssMetrics _metrics;
    private readonly ILogger<FaultInjectionControl> _logger;
    private readonly bool _startupEnabled;
    private readonly string? _startupTarget;
    private readonly IDisposable _subscription;
    private readonly object _reloadLock = new();
    private FaultInjectionSnapshot _current;
    private bool _profileConfigured;

    /// <summary>Khởi tạo từ cấu hình lúc khởi động (<paramref name="options"/>) và theo dõi <paramref name="configuration"/>.</summary>
    public FaultInjectionControl(
        IOptions<FaultInjectionOptions> options,
        IConfiguration configuration,
        IEnumerable<IValidateOptions<FaultInjectionOptions>> validators,
        TadssMetrics metrics,
        ILogger<FaultInjectionControl> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _validators = (validators ?? throw new ArgumentNullException(nameof(validators))).ToList();
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var startup = options.Value;
        _startupEnabled = startup.Enabled;
        _startupTarget = startup.TargetStrategy;
        _current = new FaultInjectionSnapshot(startup.Profile, startup.DelayMs);
        _profileConfigured = ProfileIsConfigured();
        _subscription = ChangeToken.OnChange(_configuration.GetReloadToken, Reload);
    }

    /// <summary>Cấu hình hiện hành; đọc <b>một lần</b> đầu mỗi lời gọi rồi dùng bản đó tới hết lời gọi.</summary>
    public FaultInjectionSnapshot Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();

    private void Reload()
    {
        try
        {
            lock (_reloadLock)
            {
                ReloadCore();
            }
        }
        catch (Exception exception)
        {
            // Binder (profile/số sai) hoặc lỗi bất ngờ: callback không được ném, giữ bản cũ
            RecordFailure([exception.GetType().Name + ": " + exception.Message]);
        }
    }

    private void ReloadCore()
    {
        var section = _configuration.GetSection(FaultInjectionOptions.SectionName);
        if (!section.Exists())
        {
            // Section biến mất lúc chạy (file bị xóa hoặc cụt nên nguồn JSON xóa dữ liệu): không phải ý định đổi cấu hình — muốn tắt lỗi thì ghi Profile = None
            RecordFailure([$"section {FaultInjectionOptions.SectionName} không còn trong cấu hình."]);
            return;
        }

        var profileConfigured = ProfileIsConfigured();
        if (_profileConfigured && !profileConfigured)
        {
            // Khóa Profile từng có nay mất (file cụt thì nguồn JSON xóa dữ liệu nhưng Enabled từ nguồn khác vẫn còn): bind sẽ âm thầm trả về None và gỡ lỗi đang tiêm
            RecordFailure([$"khóa {FaultInjectionOptions.SectionName}:{nameof(FaultInjectionOptions.Profile)} không còn trong cấu hình; muốn tắt lỗi hãy đặt Profile = None."]);
            return;
        }

        var fresh = section.Get<FaultInjectionOptions>() ?? new FaultInjectionOptions();
        var failures = Validate(fresh);
        if (failures.Count > 0)
        {
            RecordFailure(failures);
            return;
        }

        if (fresh.Enabled != _startupEnabled || (fresh.TargetStrategy ?? string.Empty) != (_startupTarget ?? string.Empty))
        {
            _logger.LogInformation(
                "{Section}: Enabled/TargetStrategy đổi lúc chạy chỉ có hiệu lực sau khi khởi động lại (đang giữ Enabled={Enabled}, TargetStrategy={Target}).",
                FaultInjectionOptions.SectionName,
                _startupEnabled,
                _startupTarget ?? "(mọi strategy)");
        }

        _profileConfigured = profileConfigured;
        var next = new FaultInjectionSnapshot(fresh.Profile, fresh.DelayMs);
        var previous = Current;
        if (next == previous)
        {
            return;
        }

        Volatile.Write(ref _current, next);
        _logger.LogWarning(
            "Tiêm lỗi đổi profile {PreviousProfile} → {Profile} (DelayMs {DelayMs}).",
            previous.Profile,
            next.Profile,
            next.DelayMs);
    }

    private bool ProfileIsConfigured() => _configuration.GetSection(FaultInjectionOptions.SectionName)[nameof(FaultInjectionOptions.Profile)] is not null;

    private List<string> Validate(FaultInjectionOptions options)
    {
        var failures = new List<string>();
        foreach (var validator in _validators)
        {
            var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
            if (result.Failed)
            {
                failures.AddRange(result.Failures ?? [result.FailureMessage ?? $"{FaultInjectionOptions.SectionName} không hợp lệ."]);
            }
        }

        return failures;
    }

    private void RecordFailure(IReadOnlyList<string> failures)
    {
        _metrics.RecordConfigReloadFailure(FaultInjectionOptions.SectionName);
        _logger.LogError(
            "Nạp lại {Section} thất bại, giữ cấu hình tiêm lỗi cũ (Profile {Profile}, DelayMs {DelayMs}): {Failures}",
            FaultInjectionOptions.SectionName,
            Current.Profile,
            Current.DelayMs,
            string.Join(" | ", failures));
    }
}
