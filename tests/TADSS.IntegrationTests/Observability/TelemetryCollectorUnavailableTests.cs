using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using TADSS.IntegrationTests.Infrastructure;
using Xunit;

namespace TADSS.IntegrationTests.Observability;

/// <summary>
/// OB-5 (O-03): collector không tới được thì request không bị ảnh hưởng — exporter OTLP chạy nền, lỗi chỉ ghi log nội bộ của SDK. Endpoint trỏ tới một cổng đóng của loopback;
/// 20 request đều 200, độ trễ p95 không lớn hơn lượt đối chứng tắt telemetry quá 100 ms và host dừng trong 10 giây.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class TelemetryCollectorUnavailableTests : DecisionApiTestBase
{
    private const int Requests = 20;
    private static readonly TimeSpan MaxP95Overhead = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaxShutdown = TimeSpan.FromSeconds(10);

    public TelemetryCollectorUnavailableTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <inheritdoc />
    protected override bool UseFakeClock => false;

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> EarlySettings =>
    [
        new("Tadss:Telemetry:Enabled", "true"),
        new("Tadss:Telemetry:OtlpEndpoint", "http://127.0.0.1:1"),
    ];

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string?>> ConfigurationOverrides =>
    [
        new("Tadss:Telemetry:Enabled", "true"),
        new("Tadss:Telemetry:OtlpEndpoint", "http://127.0.0.1:1"),
        new("Tadss:Telemetry:MetricExportIntervalMs", "1000"),
    ];

    /// <summary>20 POST tuần tự tới host bật telemetry (collector chết) và tới host đối chứng tắt telemetry: mọi request 200, chênh lệch p95 ≤ 100 ms, host dừng ≤ 10 giây.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task PostDecision_CollectorUnreachable_ServesNormally()
    {
        using var control = CreateFactory(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("Tadss:Telemetry:Enabled", "false")])));
        using var controlClient = control.CreateClient();
        await Infrastructure.FlushRedisAsync();

        // Khởi động nóng hai host trước khi đo
        await PostDecisionAsync(UniqueBody(0), NewKey());
        await PostDecisionAsync(UniqueBody(1), NewKey(), client: controlClient);
        var enabled = await MeasureAsync(Api, offset: 100);
        var disabled = await MeasureAsync(controlClient, offset: 200);

        enabled.Statuses.Should().OnlyContain(status => status == HttpStatusCode.OK);
        disabled.Statuses.Should().OnlyContain(status => status == HttpStatusCode.OK);
        P95(enabled.Durations).Should().BeLessThanOrEqualTo(P95(disabled.Durations) + MaxP95Overhead);

        var stopwatch = Stopwatch.StartNew();
        await Host.DisposeAsync();
        stopwatch.Elapsed.Should().BeLessThanOrEqualTo(MaxShutdown, "exporter đang thất bại không được giữ host khi dừng");
    }

    private static string UniqueBody(int offsetMinutes) =>
        DecisionRequests.StartingAt(DateTimeOffset.UtcNow.AddDays(14).AddMinutes(offsetMinutes));

    private async Task<(List<HttpStatusCode> Statuses, List<TimeSpan> Durations)> MeasureAsync(HttpClient client, int offset)
    {
        var statuses = new List<HttpStatusCode>();
        var durations = new List<TimeSpan>();
        for (var index = 0; index < Requests; index++)
        {
            var started = Stopwatch.GetTimestamp();
            var response = await PostDecisionAsync(UniqueBody(offset + index), NewKey(), client: client);
            durations.Add(Stopwatch.GetElapsedTime(started));
            statuses.Add(response.Status);
        }

        return (statuses, durations);
    }

    private static TimeSpan P95(List<TimeSpan> durations) =>
        durations.OrderBy(duration => duration).ElementAt((int)Math.Ceiling(durations.Count * 0.95) - 1);
}
