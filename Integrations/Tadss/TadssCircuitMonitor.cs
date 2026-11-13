namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Tiến trình nền của mạch ngắt (P6-57): kiểm xác nhận ngay khi có lần gọi lỗi đầu tiên, và kiểm <c>/health/ready</c> mỗi
/// <c>CircuitProbeIntervalSeconds</c> khi mạch không đóng; mỗi chu kỳ cũng chuyển mạch sang nửa mở khi tới mốc lịch. Chỉ một lần kiểm tại một
/// thời điểm. Mạch tắt hoặc đóng thì không gọi TADSS.
/// </summary>
public sealed class TadssCircuitMonitor : BackgroundService
{
    private readonly TadssCircuit _circuit;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<TadssCircuitMonitor> _logger;

    public TadssCircuitMonitor(TadssCircuit circuit, IServiceScopeFactory scopes, TimeProvider time, ILogger<TadssCircuitMonitor> logger)
    {
        _circuit = circuit;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await WaitAsync(stoppingToken);
                _circuit.Tick();
                if (!_circuit.ShouldProbe)
                    continue;
                // Lấy probe mới mỗi lần: HttpClient của typed client không được giữ suốt đời tiến trình (đổi DNS, xoay handler)
                using var scope = _scopes.CreateScope();
                var probe = scope.ServiceProvider.GetRequiredService<ITadssHealthProbe>();
                _circuit.RecordProbe(await probe.IsReadyAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Lỗi ngoài dự kiến không được làm dừng tiến trình: mạch vẫn đóng được bằng đơn thử theo mốc lịch
                _logger.LogError(ex, "Tiến trình kiểm TADSS của mạch ngắt lỗi; thử lại ở chu kỳ sau");
            }
        }
    }

    // Thức khi hết một chu kỳ hoặc khi mạch yêu cầu kiểm xác nhận, tùy điều nào tới trước
    private async Task WaitAsync(CancellationToken stoppingToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var delay = Task.Delay(_circuit.ProbeInterval, _time, wait.Token);
        var requested = _circuit.WaitForProbeRequestAsync(wait.Token);
        await Task.WhenAny(delay, requested);
        await wait.CancelAsync();
        try
        {
            await Task.WhenAll(delay, requested);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Nhánh thua bị hủy: bình thường
        }
        stoppingToken.ThrowIfCancellationRequested();
    }
}
