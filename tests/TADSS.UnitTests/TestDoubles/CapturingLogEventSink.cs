using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TADSS.UnitTests.TestDoubles;

/// <summary>
/// Sink Serilog trong bộ nhớ: ghi lại mọi <see cref="LogEvent"/> (kể cả property từ <c>LogContext</c>) để test kiểm tra nội dung log của host thật.
/// </summary>
internal sealed class CapturingLogEventSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    /// <summary>Các sự kiện log đã nhận, theo thứ tự.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>
    /// Toàn bộ văn bản của một sự kiện (message đã render, template, mọi property và exception) để kiểm tra "không chứa chuỗi X".
    /// </summary>
    public static string FullText(LogEvent logEvent) =>
        string.Join(
            '\n',
            [
                logEvent.RenderMessage(),
                logEvent.MessageTemplate.Text,
                .. logEvent.Properties.Select(pair => $"{pair.Key}={pair.Value}"),
                logEvent.Exception?.ToString() ?? string.Empty
            ]);

    /// <summary>
    /// Dẫn xuất host từ <paramref name="baseFactory"/> mà <c>ILoggerFactory</c> ghi vào <paramref name="sink"/> (mức Verbose, có <c>LogContext</c>),
    /// thay logger Serilog đọc từ cấu hình của app.
    /// </summary>
    public static WebApplicationFactory<Program> Attach(WebApplicationFactory<Program> baseFactory, CapturingLogEventSink sink)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        return baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(_ => new SerilogLoggerFactory(logger, dispose: false));
        }));
    }
}
