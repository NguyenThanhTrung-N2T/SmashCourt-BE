using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Infrastructure.Telemetry;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job kiểm tính đúng định kỳ trên bản deploy: soát mọi đơn còn hiệu lực cộng đơn tạo / sửa trong 24 giờ, bỏ đơn vừa đổi dưới 2 phút; vi phạm mới một
/// dòng log Error kèm mã đơn, vi phạm còn tồn tại chỉ nhắc trong dòng tổng (Warning); gauge theo điều có số cả khi sạch; lỗi đọc DB không được làm gauge
/// về 0 và không ném ra Hangfire.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "OB-BE-5")]
public sealed class BookingInvariantJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBookingInvariantService> _invariants = new();
    private readonly ListLogger<BookingInvariantJob> _logger = new();
    private readonly System.Diagnostics.Metrics.IMeterFactory _meters = MeterProbe.NewFactory();
    private readonly BookingInvariantMetrics _metrics;
    private readonly MeterProbe _probe;

    public BookingInvariantJobTests()
    {
        _metrics = new BookingInvariantMetrics(_meters);
        _probe = new MeterProbe(_meters, BookingInvariantMetrics.MeterName);
    }

    public void Dispose() => _probe.Dispose();

    private BookingInvariantJob Job() => new(_invariants.Object, _metrics, new FakeTimeProvider(Now), _logger);

    private Dictionary<string, long> Gauges() =>
        _probe.ObserveGauges(BookingInvariantMetrics.ViolationsInstrument).ToDictionary(p => (string)p.Tags["rule"]!, p => p.Value);

    [Fact]
    public async Task CheckRecentBookingsAsync_ChecksTheLastDay_SkippingOrdersChangedInTheLastTwoMinutes()
    {
        BookingScope? used = null;
        _invariants.Setup(s => s.CheckAsync(It.IsAny<BookingScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<BookingScope, DateTime, CancellationToken>((scope, _, _) => used = scope)
            .ReturnsAsync(new InvariantCheckResult(0, []));

        await Job().CheckRecentBookingsAsync();

        var now = Now.UtcDateTime;
        Assert.NotNull(used);
        Assert.Null(used.BookingIds);
        Assert.Equal(now.AddHours(-24), used.FromUtc);
        Assert.Equal(now, used.ToUtc);
        Assert.Equal(now.AddMinutes(-2), used.SettledBeforeUtc);
        Assert.True(used.IncludeActive, "đơn còn hiệu lực phải luôn được soát, kể cả khi đã yên quá 24 giờ");
        _invariants.Verify(s => s.CheckAsync(It.IsAny<BookingScope>(), now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckRecentBookingsAsync_WhenClean_ReportsZeroForEveryRule()
    {
        _invariants.Setup(s => s.CheckAsync(It.IsAny<BookingScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvariantCheckResult(37, []));

        await Job().CheckRecentBookingsAsync();

        var gauges = Gauges();
        Assert.Equal(InvariantRules.DatabaseRules.Order(StringComparer.Ordinal), gauges.Keys.Order(StringComparer.Ordinal));
        Assert.All(gauges.Values, v => Assert.Equal(0, v));
        Assert.Equal(37, Assert.Single(_probe.ObserveGauges(BookingInvariantMetrics.CheckedBookingsInstrument)).Value);
        Assert.Equal(BookingInvariantMetrics.OutcomeClean, Assert.Single(_probe.Recorded(BookingInvariantMetrics.RunsInstrument)).Tags["outcome"]);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task CheckRecentBookingsAsync_WhenViolated_LogsEachViolationWithTheBookingAndCountsPerRule()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _invariants.Setup(s => s.CheckAsync(It.IsAny<BookingScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvariantCheckResult(5,
            [
                new InvariantViolation(InvariantRules.NoDoubleBooking, first, "trùng giờ"),
                new InvariantViolation(InvariantRules.NoDoubleBooking, second, "trùng giờ"),
                new InvariantViolation(InvariantRules.PaidInvoiceIsCollected, first, "thu thiếu"),
            ]));

        await Job().CheckRecentBookingsAsync();

        var gauges = Gauges();
        Assert.Equal(2, gauges[InvariantRules.NoDoubleBooking]);
        Assert.Equal(1, gauges[InvariantRules.PaidInvoiceIsCollected]);
        Assert.Equal(0, gauges[InvariantRules.ConfirmedIsPaid]);
        Assert.Equal(BookingInvariantMetrics.OutcomeViolations, Assert.Single(_probe.Recorded(BookingInvariantMetrics.RunsInstrument)).Tags["outcome"]);
        var errors = _logger.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).ToList();
        Assert.Contains(errors, m => m.Contains(first.ToString()) && m.Contains(InvariantRules.PaidInvoiceIsCollected) && m.Contains("thu thiếu"));
        Assert.Contains(errors, m => m.Contains(second.ToString()) && m.Contains(InvariantRules.NoDoubleBooking));
        Assert.Contains(errors, m => m.Contains("3 vi phạm, 3 mới"));
    }

    [Fact]
    public async Task CheckRecentBookingsAsync_WhenTheCheckFails_KeepsThePreviousNumbersAndDoesNotThrow()
    {
        _invariants.SetupSequence(s => s.CheckAsync(It.IsAny<BookingScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvariantCheckResult(3, [new InvariantViolation(InvariantRules.CancelledHasReason, Guid.NewGuid(), "thiếu lý do")]))
            .ThrowsAsync(new InvalidOperationException("mất kết nối DB"));

        await Job().CheckRecentBookingsAsync();
        await Job().CheckRecentBookingsAsync();

        Assert.Equal(1, Gauges()[InvariantRules.CancelledHasReason]);
        Assert.Equal(
            [BookingInvariantMetrics.OutcomeViolations, BookingInvariantMetrics.OutcomeError],
            _probe.Recorded(BookingInvariantMetrics.RunsInstrument).Select(p => (string)p.Tags["outcome"]!));
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task CheckRecentBookingsAsync_SameViolationNextRun_IsNotLoggedAsErrorAgain_NewOneIs()
    {
        var old = new InvariantViolation(InvariantRules.CancelledHasReason, Guid.NewGuid(), "thiếu lý do");
        var added = new InvariantViolation(InvariantRules.NoDoubleBooking, Guid.NewGuid(), "trùng giờ");
        _invariants.SetupSequence(s => s.CheckAsync(It.IsAny<BookingScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvariantCheckResult(3, [old]))
            .ReturnsAsync(new InvariantCheckResult(3, [old]))
            .ReturnsAsync(new InvariantCheckResult(3, [old, added]));

        await Job().CheckRecentBookingsAsync();
        var afterFirst = _logger.Entries.Count;
        await Job().CheckRecentBookingsAsync();
        var second = _logger.Entries.Skip(afterFirst).ToList();
        var afterSecond = _logger.Entries.Count;
        await Job().CheckRecentBookingsAsync();
        var third = _logger.Entries.Skip(afterSecond).ToList();

        // Lượt 2: vi phạm cũ chỉ còn trong dòng tổng mức Warning, không có dòng Error nào
        Assert.DoesNotContain(second, e => e.Level >= LogLevel.Error);
        Assert.Contains(second, e => e.Level == LogLevel.Warning && e.Message.Contains("1 vi phạm, 0 mới"));
        // Lượt 3: chỉ vi phạm mới có dòng Error riêng; dòng tổng lên Error vì có vi phạm mới
        Assert.Contains(third, e => e.Level == LogLevel.Error && e.Message.Contains(added.BookingId.ToString()));
        Assert.DoesNotContain(third, e => e.Message.Contains(old.BookingId.ToString()));
        Assert.Contains(third, e => e.Level == LogLevel.Error && e.Message.Contains("2 vi phạm, 1 mới"));
        Assert.Equal(1, Gauges()[InvariantRules.CancelledHasReason]);
    }

    [Fact]
    public void BranchLabel_UnknownBranch_IsLabelledWithoutThrowing() =>
        Assert.Equal("unknown", BookingFlowMetrics.BranchLabel((SmashCourt_BE.Models.Enums.DepositBranch)99));

    [Fact]
    public void Metrics_BeforeTheFirstRun_ExportNothing()
    {
        Assert.Empty(_probe.ObserveGauges(BookingInvariantMetrics.ViolationsInstrument));
        Assert.Empty(_probe.ObserveGauges(BookingInvariantMetrics.CheckedBookingsInstrument));
    }
}
