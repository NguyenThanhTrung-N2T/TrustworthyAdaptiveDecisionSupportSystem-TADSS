using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting;
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
    /// thay logger Serilog đọc từ cấu hình của app. <see cref="Serilog.ILogger"/> trong DI cũng được thay (dòng log hoàn tất request dùng logger này).
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
            services.RemoveAll<Serilog.ILogger>();
            services.AddSingleton<Serilog.ILogger>(logger);
            // DiagnosticContext (thuộc tính do EnrichDiagnosticContext đặt) gắn với logger truyền vào, hoặc Log.Logger tĩnh khi là null; Log.Logger tĩnh chỉ được gán khi
            // ILoggerFactory gốc của UseSerilog được phân giải (ở đây đã bị thay) nên phải gắn tường minh, nếu không thuộc tính bị bỏ khi test chạy một mình
            var diagnosticContext = new DiagnosticContext(logger);
            services.RemoveAll<DiagnosticContext>();
            services.RemoveAll<IDiagnosticContext>();
            services.AddSingleton(diagnosticContext);
            services.AddSingleton<IDiagnosticContext>(diagnosticContext);
        }));
    }
}
