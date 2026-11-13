using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// BE-UT-14 (P6-57): tiến trình kiểm nền của mạch ngắt: kiểm xác nhận ngay sau lần gọi lỗi đầu, kiểm định kỳ khi mạch không đóng, không kiểm khi
/// mạch đóng.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-14")]
public sealed class TadssCircuitMonitorTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly TestOptionsMonitor<TadssSettings> _settings = new(TadssCircuitFactory.EnabledSettings());
    private readonly ScriptedProbe _probe = new();
    private readonly TadssCircuit _circuit;
    private readonly TadssCircuitMonitor _monitor;
    private readonly ServiceProvider _services;

    public TadssCircuitMonitorTests()
    {
        _circuit = TadssCircuitFactory.Create(_settings, _time);
        _services = new ServiceCollection().AddSingleton<ITadssHealthProbe>(_probe).BuildServiceProvider();
        _monitor = new TadssCircuitMonitor(_circuit, _services.GetRequiredService<IServiceScopeFactory>(), _time,
            NullLogger<TadssCircuitMonitor>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _monitor.StopAsync(CancellationToken.None);
        _monitor.Dispose();
        _circuit.Dispose();
        await _services.DisposeAsync();
    }

    private sealed class ScriptedProbe : ITadssHealthProbe
    {
        private readonly ConcurrentQueue<bool> _answers = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Answer(params bool[] answers)
        {
            foreach (var answer in answers)
                _answers.Enqueue(answer);
        }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_answers.TryDequeue(out var answer) && answer);
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition(), because);
    }

    private void FailOnce() => _circuit.RecordAttempt(TadssCircuitFactory.Acquire(_circuit), TadssAttemptOutcome.Failure);

    [Fact]
    public async Task Monitor_WhenFirstCallFails_ProbesImmediatelyAndOpensIfProbeFails()
    {
        _probe.Answer(false);
        await _monitor.StartAsync(CancellationToken.None);

        FailOnce();

        await EventuallyAsync(() => _circuit.State == TadssCircuitState.Open, "kiểm xác nhận lỗi phải mở mạch mà không chờ hết chu kỳ");
        Assert.Equal(1, _probe.Calls);
    }

    [Fact]
    public async Task Monitor_WhenOpen_ProbesEachIntervalAndAllowsTrialWhenProbeRecovers()
    {
        _probe.Answer(false, false, true);
        await _monitor.StartAsync(CancellationToken.None);
        FailOnce();
        await EventuallyAsync(() => _circuit.State == TadssCircuitState.Open, "mạch phải mở");

        await EventuallyAsync(() => { _time.Advance(TimeSpan.FromSeconds(10)); return _probe.Calls >= 2; }, "kiểm định kỳ khi mạch mở");
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
        await EventuallyAsync(() => { _time.Advance(TimeSpan.FromSeconds(10)); return _circuit.State == TadssCircuitState.HalfOpen; },
            "kiểm chuyển từ lỗi sang tốt phải cho đơn thử");
    }

    [Fact]
    public async Task Monitor_WhenClosed_NeverCallsTadss()
    {
        await _monitor.StartAsync(CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(20);
        }

        Assert.Equal(0, _probe.Calls);
    }
}
