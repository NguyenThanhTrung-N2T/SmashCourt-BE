using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Ghi lại log của host test (mức Warning trở lên) để test kiểm log và để lỗi 500 có stack trace (response không lộ chi tiết).
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception)
    {
        public override string ToString() => Exception == null ? $"[{Level}] {Category}: {Message}" : $"[{Level}] {Category}: {Message}\n{Exception}";
    }

    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
