using Microsoft.Extensions.Options;

namespace SmashCourt_BE.Tests.TestData;

/// <summary><see cref="IOptionsMonitor{TOptions}"/> giả: <see cref="Set"/> đổi giá trị và báo cho mọi người nghe như khi cấu hình được nạp lại.</summary>
internal sealed class TestOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public T CurrentValue { get; private set; } = initial;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        lock (_listeners)
            _listeners.Add(listener);
        return new Subscription(() =>
        {
            lock (_listeners)
                _listeners.Remove(listener);
        });
    }

    public void Set(T value)
    {
        CurrentValue = value;
        Action<T, string?>[] listeners;
        lock (_listeners)
            listeners = [.. _listeners];
        foreach (var listener in listeners)
            listener(value, Options.DefaultName);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
