using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Ghi log trạng thái cờ <c>TadssConfig:EnableTadssDecision</c> lúc khởi động và mỗi lần cờ đổi khi đang chạy (không khởi động lại),
/// để biết từ thời điểm nào đơn online mới gọi TADSS hay cọc 100 %. Nạp lại cấu hình mà cờ không đổi thì không ghi.
/// </summary>
public sealed class TadssFlagChangeLogger : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<TadssSettings> _settings;
    private readonly ILogger<TadssFlagChangeLogger> _logger;
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private bool _enabled;

    public TadssFlagChangeLogger(IOptionsMonitor<TadssSettings> settings, ILogger<TadssFlagChangeLogger> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _enabled = _settings.CurrentValue.EnableTadssDecision;
            _subscription = _settings.OnChange(OnChanged);
        }
        _logger.LogInformation("Cờ EnableTadssDecision lúc khởi động: {State}", Describe(_enabled));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _subscription?.Dispose();

    private void OnChanged(TadssSettings settings)
    {
        bool previous;
        lock (_gate)
        {
            if (settings.EnableTadssDecision == _enabled)
                return;
            previous = _enabled;
            _enabled = settings.EnableTadssDecision;
        }
        _logger.LogWarning("Cờ EnableTadssDecision đổi khi đang chạy: {Previous} ⇒ {Current}", Describe(previous), Describe(settings.EnableTadssDecision));
    }

    private static string Describe(bool enabled) => enabled ? "BẬT (đơn online mới hỏi TADSS)" : "TẮT (đơn online mới cọc 100 %)";
}
