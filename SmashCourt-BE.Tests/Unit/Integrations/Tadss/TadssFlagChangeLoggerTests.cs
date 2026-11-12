using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// BE-IT-19 ở bản deploy (12/11/2026): mỗi lần cờ <c>EnableTadssDecision</c> đổi khi đang chạy, BE ghi một dòng Warning nêu giá trị cũ và mới
/// (<c>deploy/tadss-flag.sh</c> đợi dòng này để xác nhận); nạp lại cấu hình mà cờ không đổi thì không ghi; hủy đăng ký khi dừng.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-IT-19")]
public class TadssFlagChangeLoggerTests
{
    private sealed class ManualMonitor(TadssSettings initial) : IOptionsMonitor<TadssSettings>
    {
        private readonly List<Action<TadssSettings, string?>> _listeners = [];

        public TadssSettings CurrentValue { get; private set; } = initial;

        public TadssSettings Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<TadssSettings, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(() => _listeners.Remove(listener));
        }

        public void Publish(TadssSettings settings)
        {
            CurrentValue = settings;
            foreach (var listener in _listeners.ToArray())
                listener(settings, Microsoft.Extensions.Options.Options.DefaultName);
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private static (TadssFlagChangeLogger Sut, ManualMonitor Monitor, ListLogger<TadssFlagChangeLogger> Logger) Started(bool enabled)
    {
        var monitor = new ManualMonitor(new TadssSettings { EnableTadssDecision = enabled });
        var logger = new ListLogger<TadssFlagChangeLogger>();
        var sut = new TadssFlagChangeLogger(monitor, logger);
        sut.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (sut, monitor, logger);
    }

    [Theory]
    [InlineData(false, "TẮT")]
    [InlineData(true, "BẬT")]
    public void StartAsync_LogsInitialStateAtInformation(bool enabled, string expected)
    {
        var (sut, _, logger) = Started(enabled);
        using (sut)
        {
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, entry.Level);
            Assert.Contains(expected, entry.Message);
        }
    }

    [Theory]
    [InlineData(false, true, "TẮT (đơn online mới cọc 100 %) ⇒ BẬT")]
    [InlineData(true, false, "BẬT (đơn online mới hỏi TADSS) ⇒ TẮT")]
    public void OnChange_WhenFlagFlips_LogsPreviousAndCurrentAtWarning(bool before, bool after, string expected)
    {
        var (sut, monitor, logger) = Started(before);
        using (sut)
        {
            monitor.Publish(new TadssSettings { EnableTadssDecision = after });

            var entry = logger.Entries[^1];
            Assert.Equal(2, logger.Entries.Count);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains(expected, entry.Message);
        }
    }

    [Fact]
    public void OnChange_WhenOtherSettingChangesButFlagStaysTheSame_LogsNothing()
    {
        var (sut, monitor, logger) = Started(true);
        using (sut)
        {
            monitor.Publish(new TadssSettings { EnableTadssDecision = true, TimeoutMs = 500 });

            Assert.Single(logger.Entries);
        }
    }

    [Fact]
    public void OnChange_WhenFlippedTwice_LogsEachFlipOnce()
    {
        var (sut, monitor, logger) = Started(false);
        using (sut)
        {
            monitor.Publish(new TadssSettings { EnableTadssDecision = true });
            monitor.Publish(new TadssSettings { EnableTadssDecision = true });
            monitor.Publish(new TadssSettings { EnableTadssDecision = false });

            Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning));
        }
    }

    [Fact]
    public void Dispose_StopsListeningForChanges()
    {
        var (sut, monitor, logger) = Started(false);

        sut.Dispose();
        monitor.Publish(new TadssSettings { EnableTadssDecision = true });

        Assert.Single(logger.Entries);
    }
}
