using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TADSS.Runtime.Api.Common;
using TADSS.Runtime.Api.Middlewares;
using TADSS.UnitTests.TestDoubles;
using Xunit;

namespace TADSS.UnitTests.Middlewares;

/// <summary>
/// UT-08 (C-07): <see cref="CorrelationIdMiddleware"/> chỉ chấp nhận correlation id không rỗng và dài tối đa 128 ký tự;
/// giá trị khác bị thay bằng GUID mới và giá trị dài không bao giờ lọt vào <c>TraceIdentifier</c>, log scope,
/// response header hay log (chỉ log độ dài).
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    /// <summary>Response feature ghi lại callback <c>OnStarting</c> (bản mặc định của <see cref="DefaultHttpContext"/> bỏ qua chúng).</summary>
    private sealed class CallbackCapturingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _callbacks.Add((callback, state));

        public async Task RunStartingCallbacksAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private sealed record Observation(string TraceIdentifier, string? ScopeCorrelationId, string? ResponseHeader);

    private static async Task<(Observation Observed, CapturingLogger<CorrelationIdMiddleware> Logger)> InvokeAsync(string? headerValue)
    {
        var context = new DefaultHttpContext();
        var responseFeature = new CallbackCapturingResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);
        if (headerValue is not null)
        {
            context.Request.Headers[HttpHeaderNames.CorrelationId] = headerValue;
        }

        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(
            _ =>
            {
                serilog.Information("probe");
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);
        await responseFeature.RunStartingCallbacksAsync();

        var logEvent = sink.Events.Should().ContainSingle().Subject;
        string? scopeValue = logEvent.Properties.TryGetValue("CorrelationId", out var property)
            && property is ScalarValue { Value: string text }
            ? text
            : null;
        string? responseHeader = context.Response.Headers.TryGetValue(HttpHeaderNames.CorrelationId, out var header)
            ? header.ToString()
            : null;

        return (new Observation(context.TraceIdentifier, scopeValue, responseHeader), logger);
    }

    [Theory]
    [Trait("CatalogId", "UT-08")]
    [InlineData(129)]
    [InlineData(1000)]
    public async Task Invoke_CorrelationIdOver128Chars_ReplacesWithGuid(int length)
    {
        var longValue = new string('a', length);

        var (observed, logger) = await InvokeAsync(longValue);

        Guid.TryParse(observed.TraceIdentifier, out _).Should().BeTrue();
        observed.TraceIdentifier.Should().NotBe(longValue);
        observed.ScopeCorrelationId.Should().Be(observed.TraceIdentifier);
        observed.ResponseHeader.Should().Be(observed.TraceIdentifier);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().NotContain(longValue);
        entry.Message.Should().Contain(length.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Invoke_CorrelationIdOfExactly128Chars_IsPreserved()
    {
        var value = new string('b', 128);

        var (observed, logger) = await InvokeAsync(value);

        observed.TraceIdentifier.Should().Be(value);
        observed.ScopeCorrelationId.Should().Be(value);
        observed.ResponseHeader.Should().Be(value);
        logger.Entries.Should().BeEmpty();
    }

    [Fact]
    [Trait("CatalogId", "UT-08")]
    public async Task Invoke_CorrelationIdValidOrMissing_UnchangedBehavior()
    {
        var (valid, _) = await InvokeAsync("CORR-CUSTOM-12345");
        var (missing, _) = await InvokeAsync(null);
        var (blank, _) = await InvokeAsync("   ");

        valid.TraceIdentifier.Should().Be("CORR-CUSTOM-12345");
        valid.ScopeCorrelationId.Should().Be("CORR-CUSTOM-12345");
        valid.ResponseHeader.Should().Be("CORR-CUSTOM-12345");
        Guid.TryParse(missing.TraceIdentifier, out _).Should().BeTrue();
        Guid.TryParse(blank.TraceIdentifier, out _).Should().BeTrue();
        missing.ScopeCorrelationId.Should().Be(missing.TraceIdentifier);
        blank.ResponseHeader.Should().Be(blank.TraceIdentifier);
    }
}
