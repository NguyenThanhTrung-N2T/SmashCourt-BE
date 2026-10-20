using Microsoft.Extensions.Logging;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>Logger giả giữ lại các dòng log đã render (kèm exception) để test kiểm nội dung và mức log.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception)
    {
        /// <summary>Message và exception ghép lại, dùng để chắc chắn không lộ bí mật ở bất kỳ phần nào.</summary>
        public string FullText => Exception == null ? Message : $"{Message}\n{Exception}";
    }

    private readonly List<Entry> _entries = [];

    public IReadOnlyList<Entry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}
