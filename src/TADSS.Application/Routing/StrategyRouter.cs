using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.DecisionContractSDK.Domain;
using TADSS.Application.Abstractions.Configuration;
using TADSS.Application.Common;
using TADSS.Application.Options;
using TADSS.Application.Strategies;

namespace TADSS.Application.Routing;

/// <summary>
/// Hiện thực <see cref="IStrategyRouter"/> (A-01, A-02): giữ một <see cref="RoutingTable"/> bất biến và thay nguyên khối (một tham chiếu, <see cref="Volatile"/>)
/// khi cấu hình đổi. Singleton, dispose cùng host (hủy đăng ký theo dõi cấu hình).
/// <para>
/// Cấu hình đến từ port <see cref="IRoutingRulesSource"/> (hạ tầng đọc và theo dõi nguồn cấu hình; Application không giữ <c>IConfiguration</c>).
/// Khởi động: đọc bản hiện tại (kèm lỗi binder nuốt), chạy validator, biên dịch; cấu hình sai ném <see cref="OptionsValidationException"/>, tên strategy lạ ném
/// <see cref="InvalidOperationException"/> nên ứng dụng không khởi động (<c>StartupTasks</c> resolve sớm).
/// </para>
/// <para>
/// Nạp lại nóng <b>không</b> qua <c>IOptionsMonitor</c> (lần reload sai của monitor có validator xóa giá trị đã cache và ném trước khi gọi listener, nên không
/// giữ được bảng cũ): router đăng ký <see cref="IRoutingRulesSource.Subscribe"/>, mỗi lần nguồn đổi thì đọc bản mới, chạy cùng validator, biên dịch.
/// Mọi lỗi (validator, giá trị sai kiểu mà binder nuốt — nguồn báo qua danh sách lỗi —, tên strategy lạ, exception bất ngờ) đều bị bắt: giữ bảng cũ,
/// log Error kèm danh sách lỗi và tăng <c>tadss.config.reload_failures{section="Tadss:Routing"}</c>; callback không bao giờ ném.
/// </para>
/// </summary>
public sealed class StrategyRouter : IStrategyRouter, IDisposable
{
    private readonly IRoutingRulesSource _source;
    private readonly IValidateOptions<StrategyRoutingOptions>[] _validators;
    private readonly IStrategyRegistry _registry;
    private readonly BranchCalendar _calendar;
    private readonly TadssMetrics _metrics;
    private readonly ILogger<StrategyRouter> _logger;
    private readonly IDisposable _reloadRegistration;
    private RoutingTable _table;

    /// <summary>Biên dịch bảng định tuyến khởi đầu và bắt đầu theo dõi nguồn cấu hình.</summary>
    /// <param name="source">Nguồn cấu hình <c>Tadss:Routing</c> (bản lúc khởi động và các lần nạp lại).</param>
    /// <param name="validators">Validator của <see cref="StrategyRoutingOptions"/> (chạy lại khi khởi tạo và mỗi lần nạp lại).</param>
    /// <param name="registry">Danh bạ strategy để phân giải tên.</param>
    /// <param name="calendar">Lịch cao điểm dùng chung (điều kiện <c>PeakHour</c>).</param>
    /// <param name="metrics">Meter của runtime.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="OptionsValidationException">Cấu hình không qua validator hoặc có lỗi mà binder nuốt.</exception>
    /// <exception cref="InvalidOperationException">Tên strategy (mặc định hoặc trong rule) không có trong registry.</exception>
    public StrategyRouter(
        IRoutingRulesSource source,
        IEnumerable<IValidateOptions<StrategyRoutingOptions>> validators,
        IStrategyRegistry registry,
        BranchCalendar calendar,
        TadssMetrics metrics,
        ILogger<StrategyRouter> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _validators = validators?.ToArray() ?? throw new ArgumentNullException(nameof(validators));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Binder nuốt lỗi bên trong danh sách (bỏ cả rule hoặc phần tử Channels/Tiers/BranchIds sai kiểu) nên nguồn báo riêng, như lúc nạp lại
        var bindingFailures = new List<string>();
        var initial = _source.Read(bindingFailures);
        var failures = Validate(initial);
        failures.AddRange(bindingFailures);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(StrategyRoutingOptions.SectionName, typeof(StrategyRoutingOptions), failures);
        }

        _table = RoutingTable.Compile(initial, _registry);
        _logger.LogInformation(
            "Bảng định tuyến strategy ({Section}): mặc định {DefaultStrategy}, {RuleCount} rule [{RuleNames}].",
            StrategyRoutingOptions.SectionName,
            _table.DefaultRoute.Strategy.Strategy.StrategyName,
            _table.RuleCount,
            _table.RuleNames);

        _reloadRegistration = _source.Subscribe(Reload);
    }

    /// <inheritdoc />
    public StrategyRoute Route(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return Volatile.Read(ref _table).Route(reservation, _calendar);
    }

    /// <inheritdoc />
    public void Dispose() => _reloadRegistration.Dispose();

    private void Reload()
    {
        try
        {
            var failures = new List<string>();
            var options = _source.Read(failures);
            failures.AddRange(Validate(options));
            if (failures.Count > 0)
            {
                RecordFailure(failures);
                return;
            }

            var table = RoutingTable.Compile(options, _registry);
            var previous = Interlocked.Exchange(ref _table, table);
            if (string.Equals(previous.Signature, table.Signature, StringComparison.Ordinal))
            {
                _logger.LogDebug("Cấu hình đổi nhưng bảng định tuyến {Section} không đổi.", StrategyRoutingOptions.SectionName);
                return;
            }

            _logger.LogInformation(
                "Đã nạp lại bảng định tuyến {Section}: mặc định {DefaultStrategy}, {RuleCount} rule [{RuleNames}].",
                StrategyRoutingOptions.SectionName,
                table.DefaultRoute.Strategy.Strategy.StrategyName,
                table.RuleCount,
                table.RuleNames);
        }
        catch (Exception exception)
        {
            // Binder (giá trị enum/Guid sai), tên strategy lạ hoặc lỗi bất ngờ: callback không được ném, giữ bảng cũ
            RecordFailure([exception.Message]);
        }
    }

    private List<string> Validate(StrategyRoutingOptions options)
    {
        var failures = new List<string>();
        foreach (var validator in _validators)
        {
            var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
            if (result.Failed)
            {
                failures.AddRange(result.Failures ?? [result.FailureMessage ?? $"{StrategyRoutingOptions.SectionName} không hợp lệ."]);
            }
        }

        return failures;
    }

    private void RecordFailure(IReadOnlyList<string> failures)
    {
        _metrics.RecordConfigReloadFailure(StrategyRoutingOptions.SectionName);
        _logger.LogError(
            "Nạp lại {Section} thất bại, giữ bảng định tuyến cũ (mặc định {DefaultStrategy}, {RuleCount} rule): {Failures}",
            StrategyRoutingOptions.SectionName,
            Volatile.Read(ref _table).DefaultRoute.Strategy.Strategy.StrategyName,
            Volatile.Read(ref _table).RuleCount,
            string.Join(" | ", failures));
    }
}
