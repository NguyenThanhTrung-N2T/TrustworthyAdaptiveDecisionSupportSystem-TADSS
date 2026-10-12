using System.Diagnostics.Metrics;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using TADSS.Application.Common;
using TADSS.Infrastructure.Options;
using TADSS.Runtime.Api.Security;

namespace TADSS.Runtime.Api.Composition;

/// <summary>
/// Đăng ký OpenTelemetry (O-02): trace, metric và exporter OTLP. Chỉ file trong <c>Composition/</c> được dùng kiểu <c>OpenTelemetry.*</c>
/// (UT-14); Application và Infrastructure chỉ dùng BCL (<c>ActivitySource</c>, <c>Meter</c>).
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    private const string TagUrlQuery = "url.query";
    private const string TagHttpTarget = "http.target";
    private const string MillisecondsUnit = TadssMetricNames.UnitMilliseconds;

    /// <summary>
    /// <c>Tadss:Telemetry:Enabled = false</c> (mặc định) → không đăng ký provider nào. Bật → resource O-04; trace (nguồn <c>TADSS.Runtime</c>, ASP.NET Core bỏ
    /// <c>/health/*</c> và <c>/swagger/*</c>, Npgsql, lấy mẫu <c>ParentBased(TraceIdRatioBased)</c>); metric (chỉ meter của TADSS, biên histogram theo O-05);
    /// cả hai xuất bằng OTLP chạy nền nên collector chết không ảnh hưởng request.
    /// </summary>
    public static IServiceCollection AddTadssTelemetry(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = TelemetrySettings.Read(configuration);
        if (!options.Enabled)
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(builder => builder
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: TelemetrySettings.ServiceVersion,
                    serviceInstanceId: TelemetrySettings.InstanceId(options))
                .AddAttributes([new KeyValuePair<string, object>(TelemetrySettings.EnvironmentKey, environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(TadssActivitySource.SourceName)
                .AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.Filter = context => !IsExcludedFromTracing(context.Request.Path);
                    instrumentation.RecordException = false;
                    // Không ghi query string (route có decisionId chấp nhận được, query thì không cần)
                    instrumentation.EnrichWithHttpRequest = (activity, _) =>
                    {
                        activity.SetTag(TagUrlQuery, null);
                        activity.SetTag(TagHttpTarget, null);
                    };
                })
                .AddNpgsql()
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio)))
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, options, TelemetrySettings.TracesEndpoint(options))))
            .WithMetrics(metrics => metrics
                .AddMeter(TadssMetricNames.MeterName)
                .AddView(DurationBucketView)
                .AddOtlpExporter((exporter, reader) =>
                {
                    ConfigureExporter(exporter, options, TelemetrySettings.MetricsEndpoint(options));
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = options.MetricExportIntervalMs;
                }));

        return services;
    }

    /// <summary>View áp biên bucket của O-05 cho mọi histogram có đơn vị <c>ms</c>; instrument khác giữ cấu hình mặc định (trả <c>null</c>).</summary>
    internal static MetricStreamConfiguration? DurationBucketView(Instrument instrument) =>
        instrument.Unit == MillisecondsUnit
            ? new ExplicitBucketHistogramConfiguration { Boundaries = [.. TadssMetricNames.DurationBucketBoundariesMs] }
            : null;

    private static bool IsExcludedFromTracing(PathString path) => SecurityPaths.IsHealth(path) || SecurityPaths.IsSwagger(path);

    private static void ConfigureExporter(OtlpExporterOptions exporter, TelemetryOptions options, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = options.OtlpProtocol == TelemetryOtlpProtocol.HttpProtobuf ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
    }
}
