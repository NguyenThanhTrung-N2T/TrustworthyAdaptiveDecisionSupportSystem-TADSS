using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Serilog.AspNetCore;
using Serilog.Events;
using TADSS.Application.Exceptions;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Composition;
using TADSS.Runtime.Api.Security;
using TADSS.UnitTests.Api.Security;
using TADSS.UnitTests.TestData;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Api.Composition;

/// <summary>
/// UT-14 / SEC-7: dòng log hoàn tất request nằm ngoài exception handler nên mang <b>status cuối</b> (409/404/503 do handler dựng, không còn bị ghi "500"),
/// mức log theo status và <c>ClientId</c> được làm giàu lúc kết thúc request; log không bao giờ chứa khóa API hay mã băm của nó.
/// </summary>
public class RequestLoggingTests : IClassFixture<DecisionsApiFactory>
{
    private const string CompletionTemplateMarker = "responded";
    private static readonly Guid UnknownDecisionId = Guid.Parse("7c1f5e9a-0000-4000-8000-00000000a001");

    private readonly DecisionsApiFactory _factory;

    public RequestLoggingTests(DecisionsApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDoubles();
    }

    private static IReadOnlyList<LogEvent> CompletionEvents(CapturingLogEventSink sink) =>
        sink.Events.Where(logEvent => logEvent.MessageTemplate.Text.Contains(CompletionTemplateMarker, StringComparison.Ordinal)).ToList();

    private static int StatusOf(LogEvent logEvent) => (int)((ScalarValue)logEvent.Properties["StatusCode"]).Value!;

    private static HttpRequestMessage PostDecision(string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SecurityTestHost.DecisionsPath)
        {
            Content = new StringContent(ContractSamples.RequestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.IdempotencyKey, "booking-log-1");
        request.Headers.TryAddWithoutValidation(HttpHeaderNames.CorrelationId, correlationId);
        return request;
    }

    /// <summary>
    /// 409 (khóa idempotency dùng lại) và 404 (không có quyết định) ghi Information; 503 (PostgreSQL không sẵn sàng) ghi Error; mọi trường hợp chỉ có
    /// đúng một dòng hoàn tất mang status do exception handler dựng và không có dòng nào ghi 500.
    /// </summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData(409, LogEventLevel.Information)]
    [InlineData(404, LogEventLevel.Information)]
    [InlineData(503, LogEventLevel.Error)]
    public async Task RequestLog_ExceptionMappedToStatus_LogsFinalStatus_Theory(int expectedStatus, LogEventLevel expectedLevel)
    {
        var sink = new CapturingLogEventSink();
        using var factory = CapturingLogEventSink.Attach(_factory, sink);
        using var client = factory.CreateClient();
        _factory.DecisionService.Behavior = _ => expectedStatus switch
        {
            409 => throw new IdempotencyConflictException("booking-log-1", "Khóa đã dùng với payload khác."),
            _ => throw new PersistenceUnavailableException("Mất kết nối.", new InvalidOperationException("db"))
        };
        _factory.QueryService.Behavior = _ => throw new DecisionNotFoundException(UnknownDecisionId);
        using var request = expectedStatus == 404
            ? new HttpRequestMessage(HttpMethod.Get, $"{SecurityTestHost.DecisionsPath}/{UnknownDecisionId}")
            : PostDecision("corr-log-final-status");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
        var completions = CompletionEvents(sink);
        completions.Should().ContainSingle();
        StatusOf(completions[0]).Should().Be(expectedStatus);
        completions[0].Level.Should().Be(expectedLevel);
        completions.Should().NotContain(logEvent => StatusOf(logEvent) == 500);
    }

    /// <summary>Exception lan ra khỏi pipeline (500) ghi mức Error với status 500.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public async Task RequestLog_UnhandledServerError_LogsErrorWith500()
    {
        var sink = new CapturingLogEventSink();
        using var factory = CapturingLogEventSink.Attach(_factory, sink);
        using var client = factory.CreateClient();
        _factory.DecisionService.Behavior = _ => throw new InvalidOperationException("boom-log-500");
        using var request = PostDecision("corr-log-500");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var completion = CompletionEvents(sink).Should().ContainSingle().Subject;
        StatusOf(completion).Should().Be(500);
        completion.Level.Should().Be(LogEventLevel.Error);
    }

    /// <summary>
    /// Request có khóa hợp lệ: dòng hoàn tất mang <c>ClientId</c> cấu hình; request thiếu khóa (401) thì không có thuộc tính này; không dòng log nào chứa
    /// khóa API hay mã băm SHA-256 của nó.
    /// </summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    [Trait("CatalogId", "SEC-7")]
    public async Task RequestLog_Authenticated_IncludesClientId()
    {
        var sink = new CapturingLogEventSink();
        using var factory = CapturingLogEventSink.Attach(_factory, sink);
        using var authenticated = factory.CreateClient();
        using var anonymous = TestApiKeys.CreateClientWithoutKey(factory);

        using var okResponse = await authenticated.SendAsync(PostDecision("corr-log-client"));
        using var rejected = await anonymous.SendAsync(PostDecision("corr-log-anonymous"));

        okResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var completions = CompletionEvents(sink);
        var ok = completions.Should().ContainSingle(logEvent => StatusOf(logEvent) == 200).Subject;
        ok.Properties.Should().ContainKey("ClientId", "thuộc tính hiện có: {0}", string.Join(", ", ok.Properties.Keys));
        ((ScalarValue)ok.Properties["ClientId"]).Value.Should().Be(TestApiKeys.DefaultClientId);
        var unauthorized = completions.Should().ContainSingle(logEvent => StatusOf(logEvent) == 401).Subject;
        unauthorized.Properties.Should().NotContainKey("ClientId");
        sink.Events.Select(CapturingLogEventSink.FullText)
            .Should().NotContain(text => text.Contains(TestApiKeys.DefaultKey, StringComparison.Ordinal)
                                         || text.Contains(TestApiKeys.Default.KeySha256, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Mức log theo status/exception và bộ làm giàu: kiểm trực tiếp cấu hình với <see cref="DefaultHttpContext"/>.</summary>
    [Theory]
    [Trait("CatalogId", "UT-14")]
    [InlineData(200, false, LogEventLevel.Information)]
    [InlineData(409, false, LogEventLevel.Information)]
    [InlineData(499, false, LogEventLevel.Information)]
    [InlineData(500, false, LogEventLevel.Error)]
    [InlineData(504, false, LogEventLevel.Error)]
    [InlineData(200, true, LogEventLevel.Error)]
    public void Configure_GetLevel_FollowsStatusAndException_Theory(int statusCode, bool hasException, LogEventLevel expected)
    {
        var options = new RequestLoggingOptions();
        RequestLoggingConfiguration.Configure(options);
        var context = new DefaultHttpContext();
        context.Response.StatusCode = statusCode;

        var level = options.GetLevel(context, 1.0, hasException ? new InvalidOperationException() : null);

        level.Should().Be(expected);
    }

    /// <summary>Làm giàu: chỉ gắn <c>ClientId</c> khi request đã xác thực.</summary>
    [Fact]
    [Trait("CatalogId", "UT-14")]
    public void Configure_Enrich_SetsClientIdOnlyWhenAuthenticated()
    {
        var options = new RequestLoggingOptions();
        RequestLoggingConfiguration.Configure(options);
        var context = new DefaultHttpContext();
        var anonymousCollector = new RecordingDiagnosticContext();
        var authenticatedCollector = new RecordingDiagnosticContext();

        options.EnrichDiagnosticContext!(anonymousCollector, context);
        ApiClientIdentity.Set(context, "client-x");
        options.EnrichDiagnosticContext!(authenticatedCollector, context);

        anonymousCollector.Properties.Should().BeEmpty();
        authenticatedCollector.Properties.Should().ContainKey("ClientId").WhoseValue.Should().Be("client-x");
    }

    /// <summary><see cref="Serilog.IDiagnosticContext"/> ghi lại thuộc tính được đặt.</summary>
    private sealed class RecordingDiagnosticContext : Serilog.IDiagnosticContext
    {
        public Dictionary<string, object?> Properties { get; } = [];

        public void Set(string propertyName, object value, bool destructureObjects = false) => Properties[propertyName] = value;

        public void SetException(Exception exception) => Properties["Exception"] = exception;
    }
}
