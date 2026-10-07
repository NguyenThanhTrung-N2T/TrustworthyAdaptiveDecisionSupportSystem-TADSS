using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Serilog.Events;
using TADSS.Application.Common;
using TADSS.IntegrationTests.Infrastructure;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.IntegrationTests.Observability;

/// <summary>
/// OB-5 (trace, log, metric trên host thật: PostgreSQL + Redis thật, exporter trong bộ nhớ): một request POST sinh một trace gồm span server → <c>tadss.decision.evaluate</c> →
/// <c>tadss.strategy.execute</c> cùng span Npgsql và span Redis; log ghi trong request mang trace id của span server; span và log không chứa secret;
/// tên metric xuất ra nằm trong danh mục và histogram dùng đúng biên của O-05.
/// </summary>
[Collection(TadssInfrastructureCollection.Name)]
[Trait("Category", "Integration")]
public class TelemetryExportTests : TelemetryTestBase
{
    private const string NpgsqlSourceName = "Npgsql";

    public TelemetryExportTests(TadssInfrastructureFixture infrastructure) : base(infrastructure)
    {
    }

    /// <summary>
    /// POST hợp lệ (cache miss, Redis thật): một trace có span server ASP.NET Core, <c>tadss.decision.evaluate</c> (con của span server), <c>tadss.strategy.execute</c>
    /// (con của span evaluate), span Npgsql và span Redis <c>GET</c>/<c>SET</c> cùng trace; <c>tadss.correlation_id</c> bằng header gửi; span server có <c>tadss.client_id</c>
    /// và không có query string; span Redis không ghi khóa.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task PostDecision_TelemetryEnabled_ExportsSpanTree()
    {
        await Infrastructure.FlushRedisAsync();

        var response = await PostDecisionAsync(UniqueRequest(1), NewKey(), "corr-otel-tree");

        response.Status.Should().Be(HttpStatusCode.OK);
        var trace = await WaitForTraceAsync("corr-otel-tree");
        var server = trace.Should().ContainSingle(span => span.Kind == ActivityKind.Server).Subject;
        var evaluate = trace.Should().ContainSingle(span => span.OperationName == TadssActivitySource.DecisionEvaluate).Subject;
        var execute = trace.Should().ContainSingle(span => span.OperationName == TadssActivitySource.StrategyExecute).Subject;
        evaluate.ParentSpanId.Should().Be(server.SpanId);
        execute.ParentSpanId.Should().Be(evaluate.SpanId);
        execute.GetTagItem(TadssActivitySource.TagOutcome).Should().Be(TadssMetricNames.PipelineOutcomeAutomated);
        evaluate.GetTagItem(TadssActivitySource.TagCacheHit).Should().Be(false);
        evaluate.GetTagItem(TadssActivitySource.TagStatus).Should().NotBeNull();
        server.GetTagItem(TadssActivitySource.TagClientId).Should().Be(TestApiKeys.DefaultClientId);
        server.TagObjects.Select(tag => tag.Key).Should().NotContain(["url.query", "http.target"]);
        trace.Should().Contain(span => span.Source.Name == NpgsqlSourceName, "lưu audit đi qua Npgsql");
        var redis = trace.Where(span => span.OperationName == TadssActivitySource.RedisCommand).ToList();
        redis.Select(span => span.GetTagItem(TadssActivitySource.TagDbOperation)).Should().Contain([TadssActivitySource.DbOperationGet, TadssActivitySource.DbOperationSet]);
        redis.Should().OnlyContain(span => Equals(span.GetTagItem(TadssActivitySource.TagDbSystem), TadssActivitySource.DbSystemRedis));
        redis.SelectMany(span => span.TagObjects).Select(tag => tag.Key).Should().NotContain(key => key.Contains("key", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gom mọi span và log của vài request (có khóa, thiếu khóa 401, trùng khóa idempotency 409) rồi tìm khóa API, mã băm của nó và chuỗi kết nối: không giá trị nào chứa chúng.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "SEC-7")]
    [Trait("CatalogId", "OB-5")]
    public async Task Telemetry_NoSecretsInSpansOrLogs()
    {
        var sink = new CapturingLogEventSink();
        using var logged = CapturingLogEventSink.Attach(Host, sink);
        using var client = logged.CreateClient();
        using var anonymous = TestApiKeys.CreateClientWithoutKey(logged);
        await Infrastructure.FlushRedisAsync();
        var key = NewKey();

        (await PostDecisionAsync(UniqueRequest(2), key, "corr-otel-secrets-1", client)).Status.Should().Be(HttpStatusCode.OK);
        (await PostDecisionAsync(UniqueRequest(3), key, "corr-otel-secrets-2", client)).Status.Should().Be(HttpStatusCode.Conflict);
        (await PostDecisionAsync(UniqueRequest(2), key, "corr-otel-secrets-3", anonymous)).Status.Should().Be(HttpStatusCode.Unauthorized);

        await WaitForTraceAsync("corr-otel-secrets-1");
        var forbidden = new[]
        {
            TestApiKeys.DefaultKey,
            TestApiKeys.Default.KeySha256,
            ConnectionString,
            Infrastructure.RedisConnectionString,
            "Password=",
        };
        var texts = Spans.SelectMany(span => span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}").Append(span.StatusDescription ?? string.Empty))
            .Concat(sink.Events.Select(CapturingLogEventSink.FullText))
            .ToList();
        texts.Should().NotBeEmpty();
        foreach (var secret in forbidden)
        {
            texts.Should().NotContain(text => text.Contains(secret, StringComparison.OrdinalIgnoreCase), "không secret nào được vào span hoặc log");
        }
    }

    /// <summary>Log ghi trong một request (qua logger của ứng dụng) mang <c>TraceId</c> của span server của request đó.</summary>
    [Fact]
    [Trait("CatalogId", "OB-5")]
    public async Task Logs_CarryTraceContext()
    {
        var sink = new CapturingLogEventSink();
        using var logged = CapturingLogEventSink.Attach(Host, sink);
        using var client = logged.CreateClient();
        await Infrastructure.FlushRedisAsync();

        (await PostDecisionAsync(UniqueRequest(4), NewKey(), "corr-otel-logs", client)).Status.Should().Be(HttpStatusCode.OK);

        var trace = await WaitForTraceAsync("corr-otel-logs");
        var server = trace.Single(span => span.Kind == ActivityKind.Server);
        var inRequest = sink.Events
            .Where(logEvent => logEvent.TraceId is not null && logEvent.TraceId.Value.ToString() == server.TraceId.ToString())
            .ToList();
        inRequest.Should().NotBeEmpty("log của request phải mang trace id của span server");
        inRequest.Should().Contain(logEvent => logEvent.Level == LogEventLevel.Information
                                               && logEvent.MessageTemplate.Text.Contains("responded", StringComparison.Ordinal));
    }
}
