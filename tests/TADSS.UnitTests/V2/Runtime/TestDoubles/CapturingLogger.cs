using Microsoft.Extensions.Logging;

namespace TADSS.UnitTests.V2.Runtime.TestDoubles;

/// <summary>
/// Logger viết tay ghi lại mọi bản ghi log để test kiểm tra mức log và nội dung (ví dụ cảnh báo phải kèm khóa cache).
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    /// <summary>Các bản ghi đã nhận, theo thứ tự.</summary>
    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
