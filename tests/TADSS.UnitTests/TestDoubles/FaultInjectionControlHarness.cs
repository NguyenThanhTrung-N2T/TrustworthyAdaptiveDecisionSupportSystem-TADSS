using Microsoft.Extensions.Configuration;
using TADSS.Application.Common;
using TADSS.Infrastructure.FaultInjection;
using TADSS.Infrastructure.Options;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Dựng một <see cref="FaultInjectionControl"/> thật trên cấu hình trong bộ nhớ có phát tín hiệu nạp lại, kèm metric và logger bắt được: test đổi cấu hình bằng <see cref="Set"/>
/// (thay <b>nguyên</b> section trong một bước, như ghi nguyên tử một file) hay <see cref="SetRaw"/> (đổi một khóa thô, có thể sai) rồi xem control phản ứng thế nào.
/// Tín hiệu nạp lại được gọi đồng bộ trên luồng ghi nên control đọc cấu hình mà không bị xen ngang. Dispose giải phóng control và metric.
/// </summary>
internal sealed class FaultInjectionControlHarness : IDisposable
{
    private const string Section = FaultInjectionOptions.SectionName;

    private readonly ReplaceableProvider _provider = new();

    /// <summary>Dựng control từ cấu hình ban đầu <paramref name="initial"/> (lúc khởi động).</summary>
    public FaultInjectionControlHarness(FaultInjectionOptions initial)
    {
        _provider.Replace(ToData(initial));
        Configuration = new ConfigurationBuilder().Add(new ReplaceableSource(_provider)).Build();
        Metrics = new TadssMetrics();
        Logs = new CapturingLogger<FaultInjectionControl>();
        Control = new FaultInjectionControl(
            Microsoft.Extensions.Options.Options.Create(initial),
            Configuration,
            [new FaultInjectionOptionsValidator()],
            Metrics,
            Logs);
    }

    /// <summary>Cấu hình trong bộ nhớ mà control theo dõi.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Metric của runtime dùng cho control (đọc bằng <see cref="MetricRecorder"/>).</summary>
    public TadssMetrics Metrics { get; }

    /// <summary>Log của control.</summary>
    public CapturingLogger<FaultInjectionControl> Logs { get; }

    /// <summary>Control đang được kiểm tra.</summary>
    public FaultInjectionControl Control { get; }

    /// <summary>Thay nguyên section bằng cấu hình hợp lệ mới và phát tín hiệu nạp lại.</summary>
    public void Set(FaultInjectionOptions options) => _provider.Replace(ToData(options));

    /// <summary>Xóa sạch section (như file bị xóa hoặc cụt) và phát tín hiệu nạp lại.</summary>
    public void Clear() => _provider.Replace(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Đổi một khóa thô (có thể sai) của section, giữ các khóa còn lại, rồi phát tín hiệu nạp lại.</summary>
    public void SetRaw(string key, string? value)
    {
        var data = _provider.Snapshot();
        data[$"{Section}:{key}"] = value;
        _provider.Replace(data);
    }

    private static Dictionary<string, string?> ToData(FaultInjectionOptions options) => new(StringComparer.OrdinalIgnoreCase)
    {
        [$"{Section}:{nameof(options.Enabled)}"] = options.Enabled.ToString(),
        [$"{Section}:{nameof(options.TargetStrategy)}"] = options.TargetStrategy,
        [$"{Section}:{nameof(options.DelayMs)}"] = options.DelayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [$"{Section}:{nameof(options.Profile)}"] = options.Profile.ToString(),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        Control.Dispose();
        Metrics.Dispose();
    }

    private sealed class ReplaceableSource(ReplaceableProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class ReplaceableProvider : ConfigurationProvider
    {
        public void Replace(Dictionary<string, string?> data)
        {
            Data = data;
            OnReload();
        }

        public Dictionary<string, string?> Snapshot() => new(Data, StringComparer.OrdinalIgnoreCase);
    }
}
