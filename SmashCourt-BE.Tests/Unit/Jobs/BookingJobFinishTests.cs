using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job-02 kết thúc giờ chơi có điều kiện (gói 6A-5 Task 5, B7 — P6-25). Mỗi đơn một transaction: khóa booking ⇒ đọc lại trạng thái booking và
/// trạng thái hóa đơn dưới khóa ⇒ đã đổi (nhân viên vừa check-out) thì bỏ qua, không ghi gì ⇒ câu cập nhật có điều kiện sang COMPLETED
/// (hóa đơn PAID) hoặc PENDING_PAYMENT ⇒ tắt booking_courts, trả sân theo luật cũ ⇒ commit ⇒ SignalR. Giữ nguyên luật chọn trạng thái đích,
/// mốc hết giờ (giờ kết thúc ≤ now), sự kiện cũ.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingJobFinishTests
{
    private static readonly DateTime Now = BookingJobTestBuilder.DefaultNow.UtcDateTime; // 17:00 giờ VN
    private readonly BookingJobTestBuilder _builder = new();

    private void GivenCandidates(params Booking[] bookings) =>
        _builder.BookingRepository.Setup(x => x.GetPlayEndCandidatesAsync(BookingJobTestBuilder.Today)).ReturnsAsync(bookings.ToList());

    /// <summary>Đọc lại dưới khóa: trạng thái booking và trạng thái hóa đơn hiện tại; mặc định câu cập nhật có điều kiện thắng.</summary>
    private void GivenUnderLock(Booking booking, BookingStatus status, InvoicePaymentStatus? invoiceStatus, bool endPlayWins = true)
    {
        _builder.BookingRepository.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(status);
        _builder.InvoiceRepository.Setup(x => x.GetPaymentStatusByBookingIdAsync(booking.Id)).ReturnsAsync(invoiceStatus);
        _builder.BookingRepository.Setup(x => x.EndPlayAsync(booking.Id, status, It.IsAny<BookingStatus>(), Now)).ReturnsAsync(endPlayWins);
    }

    private static Booking Ended(TimeOnly? end = null, int courtCount = 1, InvoicePaymentStatus invoiceStatus = InvoicePaymentStatus.PARTIALLY_PAID) =>
        BookingJobTestBuilder.Booking(BookingStatus.IN_PROGRESS, new TimeOnly(15, 0), end ?? new TimeOnly(16, 0), courtCount, invoiceStatus: invoiceStatus);

    // ── Chọn đơn: hết giờ khi giờ kết thúc ≤ now (như cũ) ─────────────────

    [Theory]
    [InlineData(16, 59, true)]
    [InlineData(17, 0, true)]
    [InlineData(17, 1, false)]
    public async Task ProcessExpired_OnlyOrdersWhosePlayHasEnded(int hour, int minute, bool processed)
    {
        var booking = Ended(new TimeOnly(hour, minute));
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.LockAsync(booking.Id), processed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task ProcessExpired_AsksForCandidatesOfTodayInVietnam()
    {
        GivenCandidates();

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.GetPlayEndCandidatesAsync(BookingJobTestBuilder.Today), Times.Once);
        _builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
    }

    // ── Trạng thái đích theo hóa đơn đọc dưới khóa ────────────────────────

    [Fact]
    public async Task ProcessExpired_InvoicePaidUnderLock_CompletesAndPublishesCompleted()
    {
        // Ứng viên đọc trước khi khóa thấy hóa đơn PARTIALLY_PAID; dưới khóa hóa đơn đã PAID ⇒ theo dữ liệu dưới khóa
        var booking = Ended(invoiceStatus: InvoicePaymentStatus.PARTIALLY_PAID);
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.EndPlayAsync(booking.Id, BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED, Now), Times.Once);
        _builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingCompleted,
            It.Is<BookingNotificationDto>(d => d.Status == nameof(BookingStatus.COMPLETED) && d.Timestamp == Now), booking, false), Times.Once);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
    }

    [Theory]
    [InlineData(InvoicePaymentStatus.PARTIALLY_PAID)]
    [InlineData(InvoicePaymentStatus.UNPAID)]
    [InlineData(null)]
    public async Task ProcessExpired_InvoiceNotPaidUnderLock_WaitsForPaymentAndPublishesPendingPayment(InvoicePaymentStatus? invoiceStatus)
    {
        var booking = Ended();
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, invoiceStatus);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.EndPlayAsync(booking.Id, BookingStatus.IN_PROGRESS, BookingStatus.PENDING_PAYMENT, Now), Times.Once);
        _builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingPendingPayment,
            It.IsAny<BookingNotificationDto>(), booking, false), Times.Once);
    }

    [Fact]
    public async Task ProcessExpired_RunsLockThenReReadThenConditionalUpdateThenRelease()
    {
        var booking = Ended();
        GivenCandidates(booking);
        var calls = new List<string>();
        _builder.BookingRepository.Setup(x => x.LockAsync(booking.Id)).Callback(() => calls.Add("lock")).Returns(Task.CompletedTask);
        _builder.BookingRepository.Setup(x => x.GetBookingStatusAsync(booking.Id)).Callback(() => calls.Add("status")).ReturnsAsync(BookingStatus.IN_PROGRESS);
        _builder.InvoiceRepository.Setup(x => x.GetPaymentStatusByBookingIdAsync(booking.Id)).Callback(() => calls.Add("invoice"))
            .ReturnsAsync(InvoicePaymentStatus.PAID);
        _builder.BookingRepository.Setup(x => x.EndPlayAsync(booking.Id, BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED, Now))
            .Callback(() => calls.Add("end")).ReturnsAsync(true);
        _builder.BookingRepository.Setup(x => x.UpdateCourtActiveStatusAsync(booking.Id, false)).Callback(() => calls.Add("deactivate")).Returns(Task.CompletedTask);
        _builder.CourtRepository.Setup(x => x.ReleaseToAvailableAsync(It.IsAny<IReadOnlyCollection<Guid>>(), Now))
            .Callback(() => calls.Add("release")).Returns(Task.CompletedTask);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        Assert.Equal(["lock", "status", "invoice", "end", "deactivate", "release"], calls);
    }

    // ── Thua: nhân viên vừa check-out ─────────────────────────────────────

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]        // check-out đã thu đủ
    [InlineData(BookingStatus.PENDING_PAYMENT)]  // lần chạy trước (instance khác) đã kết thúc
    public async Task ProcessExpired_StatusChangedBeforeLock_SkipsWithoutWriting(BookingStatus current)
    {
        var booking = Ended();
        GivenCandidates(booking);
        GivenUnderLock(booking, current, InvoicePaymentStatus.PAID);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.EndPlayAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()), Times.Never);
        _builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        _builder.CourtRepository.Verify(x => x.ReleaseToAvailableAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>()), Times.Never);
        _builder.Broadcast.VerifyNoOtherCalls();
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
    }

    [Fact]
    public async Task ProcessExpired_ConditionalUpdateLoses_SkipsWithoutReleasing()
    {
        var booking = Ended();
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID, endPlayWins: false);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        _builder.Broadcast.VerifyNoOtherCalls();
    }

    // ── Nhả sân theo luật cũ ──────────────────────────────────────────────

    [Fact]
    public async Task ProcessExpired_CourtHeldByAnotherOrder_IsNotReleased()
    {
        var booking = Ended(courtCount: 2);
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);
        var courtIds = booking.BookingCourts.Select(bc => bc.CourtId).ToList();
        _builder.BookingRepository.Setup(x => x.GetCourtsHeldByOtherBookingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), booking.Id))
            .ReturnsAsync([courtIds[1]]);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(booking.Id, false), Times.Once);
        _builder.CourtRepository.Verify(x => x.ReleaseToAvailableAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { courtIds[0] })), Now), Times.Once);
    }

    // ── Một đơn lỗi không chặn đơn khác; log tổng ─────────────────────────

    [Fact]
    public async Task ProcessExpired_OneOrderThrows_NextIsStillProcessed()
    {
        var broken = Ended();
        var next = Ended();
        GivenCandidates(broken, next);
        _builder.BookingRepository.Setup(x => x.LockAsync(broken.Id)).ThrowsAsync(new InvalidOperationException("DB hỏng"));
        GivenUnderLock(next, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.EndPlayAsync(next.Id, BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED, Now), Times.Once);
        Assert.Contains(_builder.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(broken.Id.ToString()));
    }

    [Fact]
    public async Task ProcessExpired_LogsCompletedPendingPaymentSkippedAndFailed()
    {
        var completed = Ended();
        var pendingPayment = Ended();
        var skipped = Ended();
        var failed = Ended();
        GivenCandidates(completed, pendingPayment, skipped, failed);
        GivenUnderLock(completed, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);
        GivenUnderLock(pendingPayment, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PARTIALLY_PAID);
        GivenUnderLock(skipped, BookingStatus.COMPLETED, InvoicePaymentStatus.PAID);
        _builder.BookingRepository.Setup(x => x.LockAsync(failed.Id)).ThrowsAsync(new TimeoutException());

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        var summary = Assert.Single(_builder.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("ứng viên"));
        Assert.Contains("4 ứng viên", summary.Message);
        Assert.Contains("hoàn tất 1", summary.Message);
        Assert.Contains("chờ thanh toán 1", summary.Message);
        Assert.Contains("bỏ qua vì đổi trạng thái 1", summary.Message);
        Assert.Contains("lỗi 1", summary.Message);
    }

    [Fact]
    public async Task ProcessExpired_CandidateQueryThrows_DoesNotThrowToHangfire()
    {
        _builder.BookingRepository.Setup(x => x.GetPlayEndCandidatesAsync(It.IsAny<DateOnly>())).ThrowsAsync(new TimeoutException());

        var ex = await Record.ExceptionAsync(() => _builder.Build().ProcessExpiredActiveBookingsAsync());

        Assert.Null(ex);
        Assert.Single(_builder.Logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ProcessExpired_OrderWithoutCourts_IsIgnored()
    {
        var booking = Ended();
        booking.BookingCourts.Clear();
        GivenCandidates(booking);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ProcessExpired_TwoCourtsWithDifferentEnds_WaitsForTheLatestEnd()
    {
        // Đơn nhiều sân: hết giờ khi sân kết thúc muộn nhất đã hết (17:00 hiện tại; một sân tới 17:30)
        var booking = Ended(new TimeOnly(16, 0), courtCount: 2);
        booking.BookingCourts.Last().EndTime = new TimeOnly(17, 30);
        GivenCandidates(booking);
        GivenUnderLock(booking, BookingStatus.IN_PROGRESS, InvoicePaymentStatus.PAID);

        await _builder.Build().ProcessExpiredActiveBookingsAsync();

        _builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
    }
}
