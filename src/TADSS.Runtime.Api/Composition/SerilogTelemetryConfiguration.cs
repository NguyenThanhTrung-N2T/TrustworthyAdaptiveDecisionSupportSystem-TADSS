using Serilog;
using Serilog.Sinks.OpenTelemetry;
using TADSS.Infrastructure.Options;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Gắn sink log OTLP (Serilog → Loki) vào cấu hình Serilog khi telemetry bật (O-08). Sink console và các sink trong section <c>Serilog</c> giữ nguyên;
/// sink OTLP dùng cùng endpoint và resource với trace/metric nên <c>Tadss:Telemetry</c> là chỗ bật/tắt duy nhất. Log ghi trong một span mang
/// <c>TraceId</c>/<c>SpanId</c> của span đó.
/// </summary>
public static class SerilogTelemetryConfiguration
{
    /// <summary>
    /// Thêm sink OTLP vào <paramref name="logger"/> khi <c>Enabled &amp;&amp; ExportLogs</c>; ngược lại trả nguyên cấu hình. Mức log theo cấu hình
    /// <c>Serilog</c> hiện có (đã đọc vào <paramref name="logger"/> trước khi gọi).
    /// </summary>
    public static LoggerConfiguration Apply(LoggerConfiguration logger, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(logger);
        var options = TelemetrySettings.Read(configuration);
        if (!options.Enabled || !options.ExportLogs)
        {
            return logger;
        }

        var resource = TelemetrySettings.ResourceAttributes(options, environment).ToDictionary(pair => pair.Key, pair => pair.Value);
        return logger.WriteTo.OpenTelemetry(sink =>
        {
            sink.Endpoint = TelemetrySettings.LogsEndpoint(options).ToString();
            sink.Protocol = options.OtlpProtocol == TelemetryOtlpProtocol.HttpProtobuf ? OtlpProtocol.HttpProtobuf : OtlpProtocol.Grpc;
            sink.ResourceAttributes = resource;
        });
    }
}
