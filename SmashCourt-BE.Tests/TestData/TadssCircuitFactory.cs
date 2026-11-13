using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>Dựng <see cref="TadssCircuit"/> cho test (P6-57). <see cref="Disabled"/> cho test không liên quan mạch: hành vi như trước P6-57.</summary>
internal static class TadssCircuitFactory
{
    /// <summary>Cấu hình bật mạch với tham số mặc định (3 lần lỗi, kiểm 10 s, 300 ms, lịch 60…300 s).</summary>
    public static TadssSettings EnabledSettings() => new()
    {
        EnableTadssDecision = true,
        BaseUrl = "http://tadss.test:5296",
        ApiKey = "key",
        CircuitEnabled = true
    };

    public static TadssCircuit Create(TestOptionsMonitor<TadssSettings> settings, TimeProvider time, ILogger<TadssCircuit>? logger = null,
        IMeterFactory? meters = null) =>
        new(settings, time, logger ?? NullLogger<TadssCircuit>.Instance, meters ?? NewMeterFactory());

    // Mỗi mạch một factory riêng: meter cùng tên của hai mạch trong một test không dùng chung instrument
    private static IMeterFactory NewMeterFactory() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    /// <summary>Xin quyền gọi; test dùng khi chắc mạch đang cấp quyền (ném lỗi nếu mạch ngắt).</summary>
    public static TadssCircuitPermit Acquire(TadssCircuit circuit) =>
        circuit.TryAcquire() ?? throw new InvalidOperationException("Mạch không cấp quyền gọi");

    public static TadssCircuit Disabled() =>
        Create(new TestOptionsMonitor<TadssSettings>(new TadssSettings()), TimeProvider.System);
}
