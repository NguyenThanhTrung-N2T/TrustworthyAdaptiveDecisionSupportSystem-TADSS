using Microsoft.Extensions.Options;

namespace TADSS.Infrastructure.Options;

/// <summary>
/// Kiểm tra <see cref="TelemetryOptions"/> khi khởi động (fail-fast, ở mọi môi trường): <c>OtlpEndpoint</c> là URI tuyệt đối http/https khi bật telemetry,
/// <c>TraceSampleRatio</c> trong [0, 1], <c>MetricExportIntervalMs</c> trong 1000..60000, <c>PendingApprovalsRefreshSeconds</c> trong 5..3600,
/// <c>ServiceName</c> không rỗng và tối đa 64 ký tự. Áp dụng cho section <c>Tadss:Telemetry</c>.
/// </summary>
public sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    /// <summary>Giới hạn dưới của <c>MetricExportIntervalMs</c> (ms).</summary>
    public const int MinMetricExportIntervalMs = 1_000;

    /// <summary>Giới hạn trên của <c>MetricExportIntervalMs</c> (ms).</summary>
    public const int MaxMetricExportIntervalMs = 60_000;

    /// <summary>Giới hạn dưới của <c>PendingApprovalsRefreshSeconds</c> (giây).</summary>
    public const int MinRefreshSeconds = 5;

    /// <summary>Giới hạn trên của <c>PendingApprovalsRefreshSeconds</c> (giây).</summary>
    public const int MaxRefreshSeconds = 3_600;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        var failures = new List<string>();
        var section = TelemetryOptions.SectionName;

        if (options.Enabled && !IsHttpUri(options.OtlpEndpoint))
        {
            failures.Add($"{section}:{nameof(options.OtlpEndpoint)} phải là URI tuyệt đối http hoặc https khi bật telemetry.");
        }

        if (options.TraceSampleRatio is < 0d or > 1d || double.IsNaN(options.TraceSampleRatio))
        {
            failures.Add($"{section}:{nameof(options.TraceSampleRatio)} phải nằm trong khoảng 0..1 (hiện là {options.TraceSampleRatio}).");
        }

        if (options.MetricExportIntervalMs is < MinMetricExportIntervalMs or > MaxMetricExportIntervalMs)
        {
            failures.Add($"{section}:{nameof(options.MetricExportIntervalMs)} phải nằm trong khoảng {MinMetricExportIntervalMs}..{MaxMetricExportIntervalMs} ms (hiện là {options.MetricExportIntervalMs}).");
        }

        if (options.PendingApprovalsRefreshSeconds is < MinRefreshSeconds or > MaxRefreshSeconds)
        {
            failures.Add($"{section}:{nameof(options.PendingApprovalsRefreshSeconds)} phải nằm trong khoảng {MinRefreshSeconds}..{MaxRefreshSeconds} giây (hiện là {options.PendingApprovalsRefreshSeconds}).");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName) || options.ServiceName.Length > TelemetryOptions.MaxServiceNameLength)
        {
            failures.Add($"{section}:{nameof(options.ServiceName)} không được rỗng và dài tối đa {TelemetryOptions.MaxServiceNameLength} ký tự.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
