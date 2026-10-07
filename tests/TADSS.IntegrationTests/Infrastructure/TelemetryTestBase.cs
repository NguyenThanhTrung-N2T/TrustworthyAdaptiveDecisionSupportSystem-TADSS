using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using TADSS.Application.Common;

namespace TADSS.IntegrationTests.Infrastructure;

/// <summary>
/// Lớp cơ sở cho test telemetry (OB-5) trên host thật: bật <c>Tadss:Telemetry</c> với endpoint không có ai nghe (collector "chết"; exporter OTLP chạy nền nên không ảnh hưởng)
/// và gắn thêm exporter <b>trong bộ nhớ</b> cho trace và metric để test đọc được span/metric đã xuất. Dùng đồng hồ thật vì Polly cần timer thật.
/// <see cref="ActivitySource"/> là toàn cục nên span của host khác (cùng process) có thể lẫn vào <see cref="Spans"/>; test lọc theo trace của chính request.
/// </summary>
public abstract class TelemetryTestBase : DecisionApiTestBase
{
    /// <summary>Endpoint không có collector nào lắng nghe (cổng 1 của loopback).</summary>
    protected const string UnreachableCollector = "http://127.0.0.1:1";

    /// <summary>Hạn chờ span/metric xuất hiện (giây).</summary>
    protected static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    private readonly List<Activity> _spans = [];

    /// <summary>Hạ tầng thật dùng chung.</summary>
    protected TelemetryTestBase(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <summary>Cấu hình bổ sung của lớp dẫn xuất (ví dụ bật shadow, tiêm lỗi); đặt sau cấu hình telemetry.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string?>> ExtraConfiguration => [];

    /// <inheritdoc />
    protected sealed override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides =>
    [
        new("Tadss:Telemetry:Enabled", "true"),
        new("Tadss:Telemetry:OtlpEndpoint", UnreachableCollector),
        new("Tadss:Telemetry:ServiceInstanceId", "tadss-it"),
        new("Tadss:Telemetry:ExportLogs", "false"),
        new("Tadss:Telemetry:MetricExportIntervalMs", "1000"),
        .. ExtraConfiguration,
    ];

    /// <inheritdoc />
    protected sealed override IEnumerable<KeyValuePair<string, string?>> EarlySettings =>
    [
        new("Tadss:Telemetry:Enabled", "true"),
        new("Tadss:Telemetry:OtlpEndpoint", UnreachableCollector),
    ];

    /// <summary>Các span đã xuất (bản sao tại thời điểm gọi).</summary>
    protected IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_spans)
            {
                return _spans.ToArray();
            }
        }
    }

    /// <inheritdoc />
    protected override void ConfigureHostServices(IServiceCollection services)
    {
        services.ConfigureOpenTelemetryTracerProvider((_, builder) => builder.AddInMemoryExporter(_spans));
    }

    /// <summary>
    /// Chờ tới khi span <c>tadss.decision.evaluate</c> mang <c>tadss.correlation_id</c> = <paramref name="correlationId"/> <b>và</b> span server của cùng trace đã xuất,
    /// rồi trả mọi span của trace đó. Span server kết thúc sau khi response được gửi nên phải chờ có hạn.
    /// </summary>
    protected async Task<IReadOnlyList<Activity>> WaitForTraceAsync(string correlationId)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (true)
        {
            var spans = Spans;
            var evaluate = spans.FirstOrDefault(span =>
                span.OperationName == TadssActivitySource.DecisionEvaluate
                && Equals(span.GetTagItem(TadssActivitySource.TagCorrelationId), correlationId));
            if (evaluate is not null)
            {
                var trace = spans.Where(span => span.TraceId == evaluate.TraceId).ToList();
                if (trace.Any(span => span.Kind == ActivityKind.Server))
                {
                    return trace;
                }
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Chưa thấy trace của correlation id {correlationId} sau {SettleTimeout.TotalSeconds} giây (có {spans.Count} span).");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>Request hợp lệ với giờ bắt đầu riêng của lớp test (tránh trùng khóa cache của test khác).</summary>
    protected static string UniqueRequest(int offsetMinutes) =>
        DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(13).AddMinutes(offsetMinutes));
}
