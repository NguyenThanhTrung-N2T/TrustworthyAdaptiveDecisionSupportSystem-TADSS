using System.Reflection;
using Microsoft.Extensions.Options;
using TADSS.Infrastructure.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Cấu hình telemetry dùng chung cho trace, metric và log (O-03, O-04): đọc + kiểm tra <see cref="TelemetryOptions"/>, dựng thuộc tính resource
/// và địa chỉ từng tín hiệu OTLP. Một nơi duy nhất nên ba đường xuất (trace, metric, log) luôn cùng resource và cùng endpoint.
/// </summary>
internal static class TelemetrySettings
{
    /// <summary>Thuộc tính resource: tên dịch vụ.</summary>
    public const string ServiceNameKey = "service.name";

    /// <summary>Thuộc tính resource: phiên bản dịch vụ.</summary>
    public const string ServiceVersionKey = "service.version";

    /// <summary>Thuộc tính resource: định danh instance (SCALE-2 tách số liệu theo instance).</summary>
    public const string ServiceInstanceIdKey = "service.instance.id";

    /// <summary>Thuộc tính resource: tên môi trường.</summary>
    public const string EnvironmentKey = "deployment.environment.name";

    private const string TracesPath = "v1/traces";
    private const string MetricsPath = "v1/metrics";
    private const string LogsPath = "v1/logs";

    /// <summary>Phiên bản của Runtime.Api (<c>AssemblyInformationalVersion</c>).</summary>
    public static string ServiceVersion { get; } =
        typeof(TelemetrySettings).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Đọc section <c>Tadss:Telemetry</c> và kiểm tra bằng <see cref="TelemetryOptionsValidator"/> ngay khi dựng host: cấu hình sai thì dừng với thông báo
    /// của validator thay vì lỗi khó hiểu từ exporter.
    /// </summary>
    public static TelemetryOptions Read(IConfiguration configuration)
    {
        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new TelemetryOptions();
        var result = new TelemetryOptionsValidator().Validate(MsOptions.DefaultName, options);
        if (result.Failed)
        {
            throw new OptionsValidationException(MsOptions.DefaultName, typeof(TelemetryOptions), result.Failures);
        }

        return options;
    }

    /// <summary><c>service.instance.id</c>: giá trị cấu hình, rỗng thì tên máy/container.</summary>
    public static string InstanceId(TelemetryOptions options) =>
        string.IsNullOrWhiteSpace(options.ServiceInstanceId) ? Environment.MachineName : options.ServiceInstanceId;

    /// <summary>Bốn thuộc tính resource của O-04.</summary>
    public static IReadOnlyDictionary<string, object> ResourceAttributes(TelemetryOptions options, IHostEnvironment environment) =>
        new Dictionary<string, object>
        {
            [ServiceNameKey] = options.ServiceName,
            [ServiceVersionKey] = ServiceVersion,
            [ServiceInstanceIdKey] = InstanceId(options),
            [EnvironmentKey] = environment.EnvironmentName,
        };

    /// <summary>Địa chỉ nhận trace theo giao thức đã chọn.</summary>
    public static Uri TracesEndpoint(TelemetryOptions options) => SignalEndpoint(options, TracesPath);

    /// <summary>Địa chỉ nhận metric theo giao thức đã chọn.</summary>
    public static Uri MetricsEndpoint(TelemetryOptions options) => SignalEndpoint(options, MetricsPath);

    /// <summary>Địa chỉ nhận log theo giao thức đã chọn.</summary>
    public static Uri LogsEndpoint(TelemetryOptions options) => SignalEndpoint(options, LogsPath);

    /// <summary>
    /// gRPC dùng nguyên địa chỉ gốc. HTTP/protobuf cần đường dẫn riêng từng tín hiệu (<c>/v1/traces</c>...): khi đặt endpoint bằng mã, SDK không tự thêm.
    /// </summary>
    private static Uri SignalEndpoint(TelemetryOptions options, string path)
    {
        var baseUri = new Uri(options.OtlpEndpoint, UriKind.Absolute);
        return options.OtlpProtocol == TelemetryOtlpProtocol.HttpProtobuf
            ? new Uri($"{baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/{path}")
            : baseUri;
    }
}
