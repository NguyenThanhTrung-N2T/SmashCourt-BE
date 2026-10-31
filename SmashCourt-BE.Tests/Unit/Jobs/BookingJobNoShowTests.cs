using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job-04 ghi NO_SHOW có điều kiện (gói 6A-5 Task 5, B6 — P6-25). Giữ ân hạn 15' và luật nhả sân cũ; mỗi đơn một transaction mở đầu bằng
/// câu cập nhật có điều kiện "trạng thái chưa đổi và checked_in_at còn trống" (vừa khóa dòng vừa quyết định bên thắng). Thua (nhân viên vừa
/// check-in hoặc vừa hủy) ⇒ không ghi gì, không thông báo. "Sân còn bận" tính sau khi thắng, chỉ loại trừ chính đơn đó.
/// Người chờ slot được báo qua dịch vụ chung (link trang chủ — B3).
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingJobNoShowTests
{
    private static readonly DateTime Now = BookingJobTestBuilder.DefaultNow.UtcDateTime; // 17:00 giờ VN
    private readonly BookingJobTestBuilder _builder = new();

    private void GivenCandidates(params Booking[] bookings) =>
        _builder.BookingRepository.Setup(x => x.GetNoShowCandidatesAsync(BookingJobTestBuilder.Today)).ReturnsAsync(bookings.ToList());

    private void GivenMarkNoShow(Booking booking, bool won) =>
        _builder.BookingRepository.Setup(x => x.MarkNoShowAsync(booking.Id, booking.Status, Now)).ReturnsAsync(won);

    private static Booking Candidate(BookingStatus status = BookingStatus.CONFIRMED, TimeOnly? start = null, int courtCount = 1) =>
        BookingJobTestBuilder.Booking(status, start ?? new TimeOnly(16, 0), new TimeOnly(18, 0), courtCount);

    // ── Chọn đơn: ân hạn 15' như cũ ──────────────────────────────────────

    [Fact]
    public async Task DetectNoShow_AsksForCandidatesOfTodayInVietnam()
    {
        GivenCandidates();

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.GetNoShowCandidatesAsync(BookingJobTestBuilder.Today), Times.Once);
        _builder.BookingRepository.Verify(x => x.MarkNoShowAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData(16, 44, true)]   // bắt đầu + 15' = 16:59 < 17:00
    [InlineData(16, 45, false)]  // bắt đầu + 15' = 17:00, chưa quá (luật cũ: < now)
    [InlineData(16, 55, false)]  // còn trong ân hạn
    public async Task DetectNoShow_OnlyOrdersPastTheGraceMinutes(int hour, int minute, bool marked)
    {
        var booking = Candidate(start: new TimeOnly(hour, minute));
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: true);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.MarkNoShowAsync(booking.Id, BookingStatus.CONFIRMED, Now), marked ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task DetectNoShow_PreviousDayOrder_IsPastGraceAllDay()
    {
        var booking = BookingJobTestBuilder.Booking(BookingStatus.PAID_ONLINE, new TimeOnly(22, 0), new TimeOnly(23, 0),
            date: BookingJobTestBuilder.Today.AddDays(-1));
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: true);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.MarkNoShowAsync(booking.Id, BookingStatus.PAID_ONLINE, Now), Times.Once);
    }

    // ── Thắng: nhả sân theo luật cũ, thông báo sau commit ─────────────────

    [Fact]
    public async Task DetectNoShow_Wins_DeactivatesCourtsReleasesFreeOnesAndRunsConditionalUpdateFirst()
    {
        var booking = Candidate(courtCount: 2);
        GivenCandidates(booking);
        var calls = new List<string>();
        _builder.BookingRepository.Setup(x => x.MarkNoShowAsync(booking.Id, booking.Status, Now)).Callback(() => calls.Add("mark")).ReturnsAsync(true);
        _builder.BookingRepository.Setup(x => x.UpdateCourtActiveStatusAsync(booking.Id, false)).Callback(() => calls.Add("deactivate")).Returns(Task.CompletedTask);
        var courtIds = booking.BookingCourts.Select(bc => bc.CourtId).ToList();
        _builder.BookingRepository.Setup(x => x.GetCourtsHeldByOtherBookingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), booking.Id))
            .Callback(() => calls.Add("held")).ReturnsAsync([courtIds[0]]);
        _builder.CourtRepository.Setup(x => x.ReleaseToAvailableAsync(It.IsAny<IReadOnlyCollection<Guid>>(), Now))
            .Callback(() => calls.Add("release")).Returns(Task.CompletedTask);

        await _builder.Build().DetectNoShowBookingsAsync();

        Assert.Equal(["mark", "deactivate", "held", "release"], calls);
        // "Sân còn bận" chỉ loại trừ chính đơn này; sân đơn khác đang giữ không được trả
        _builder.BookingRepository.Verify(x => x.GetCourtsHeldByOtherBookingsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.OrderBy(i => i).SequenceEqual(courtIds.OrderBy(i => i))), booking.Id), Times.Once);
        _builder.CourtRepository.Verify(x => x.ReleaseToAvailableAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { courtIds[1] })), Now), Times.Once);
        Assert.Equal(BookingStatus.NO_SHOW, booking.Status);
    }

    [Fact]
    public async Task DetectNoShow_EveryCourtHeldByOthers_ReleasesNone()
    {
        var booking = Candidate();
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: true);
        _builder.BookingRepository.Setup(x => x.GetCourtsHeldByOtherBookingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), booking.Id))
            .ReturnsAsync(booking.BookingCourts.Select(bc => bc.CourtId).ToList());

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.CourtRepository.Verify(x => x.ReleaseToAvailableAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task DetectNoShow_Wins_PublishesNoShowAndNotifiesSlotWaitersThroughSharedService()
    {
        var booking = Candidate();
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: true);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingNoShow,
            It.Is<BookingNotificationDto>(d => d.Status == nameof(BookingStatus.NO_SHOW) && d.BookingId == booking.Id && d.Timestamp == Now),
            booking, true), Times.Once);
        _builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(booking), Times.Once);
    }

    // ── Thua: nhân viên vừa check-in hoặc vừa hủy ─────────────────────────

    [Fact]
    public async Task DetectNoShow_LosesToCheckInOrStaffCancel_WritesAndNotifiesNothing()
    {
        var booking = Candidate();
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: false);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        _builder.CourtRepository.Verify(x => x.ReleaseToAvailableAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>()), Times.Never);
        _builder.Broadcast.VerifyNoOtherCalls();
        _builder.SlotInterestNotifier.VerifyNoOtherCalls();
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
    }

    // ── Một đơn lỗi không chặn đơn khác; log tổng ─────────────────────────

    [Fact]
    public async Task DetectNoShow_OneOrderThrows_NextIsStillMarked()
    {
        var broken = Candidate();
        var next = Candidate();
        GivenCandidates(broken, next);
        _builder.BookingRepository.Setup(x => x.MarkNoShowAsync(broken.Id, It.IsAny<BookingStatus>(), It.IsAny<DateTime>()))
            .ThrowsAsync(new InvalidOperationException("DB hỏng"));
        GivenMarkNoShow(next, won: true);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.MarkNoShowAsync(next.Id, BookingStatus.CONFIRMED, Now), Times.Once);
        Assert.Equal(BookingStatus.NO_SHOW, next.Status);
        Assert.Contains(_builder.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(broken.Id.ToString()));
        _builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingNoShow, It.IsAny<BookingNotificationDto>(), broken, It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task DetectNoShow_NotificationThrows_StillCountsAsMarked()
    {
        var booking = Candidate();
        GivenCandidates(booking);
        GivenMarkNoShow(booking, won: true);
        _builder.Broadcast.Setup(x => x.BroadcastBookingEventAsync(It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));

        var ex = await Record.ExceptionAsync(() => _builder.Build().DetectNoShowBookingsAsync());

        Assert.Null(ex);
        _builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(booking), Times.Once);
        Assert.Contains(_builder.Logger.Entries, e => e.Message.Contains("đã đánh NO_SHOW 1"));
    }

    [Fact]
    public async Task DetectNoShow_LogsCandidatesMarkedSkippedAndFailed()
    {
        var marked = Candidate();
        var skipped = Candidate();
        var failed = Candidate();
        var notDue = Candidate(start: new TimeOnly(16, 55));
        GivenCandidates(marked, skipped, failed, notDue);
        GivenMarkNoShow(marked, won: true);
        GivenMarkNoShow(skipped, won: false);
        _builder.BookingRepository.Setup(x => x.MarkNoShowAsync(failed.Id, It.IsAny<BookingStatus>(), It.IsAny<DateTime>())).ThrowsAsync(new TimeoutException());

        await _builder.Build().DetectNoShowBookingsAsync();

        var summary = Assert.Single(_builder.Logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("ứng viên"));
        Assert.Contains("3 ứng viên", summary.Message);
        Assert.Contains("đã đánh NO_SHOW 1", summary.Message);
        Assert.Contains("bỏ qua vì đổi trạng thái 1", summary.Message);
        Assert.Contains("lỗi 1", summary.Message);
    }

    [Fact]
    public async Task DetectNoShow_CandidateQueryThrows_DoesNotThrowToHangfire()
    {
        _builder.BookingRepository.Setup(x => x.GetNoShowCandidatesAsync(It.IsAny<DateOnly>())).ThrowsAsync(new TimeoutException());

        var ex = await Record.ExceptionAsync(() => _builder.Build().DetectNoShowBookingsAsync());

        Assert.Null(ex);
        Assert.Single(_builder.Logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task DetectNoShow_LosingOneOrder_DoesNotStopTheNext()
    {
        var checkedInMeanwhile = Candidate();
        var next = Candidate(BookingStatus.PAID_ONLINE);
        GivenCandidates(checkedInMeanwhile, next);
        GivenMarkNoShow(checkedInMeanwhile, won: false);
        GivenMarkNoShow(next, won: true);

        await _builder.Build().DetectNoShowBookingsAsync();

        Assert.Equal(BookingStatus.CONFIRMED, checkedInMeanwhile.Status);
        Assert.Equal(BookingStatus.NO_SHOW, next.Status);
        _builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(next), Times.Once);
        _builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(checkedInMeanwhile), Times.Never);
    }

    [Fact]
    public async Task DetectNoShow_OrderWithoutCourts_IsIgnored()
    {
        var booking = Candidate();
        booking.BookingCourts.Clear();
        GivenCandidates(booking);

        await _builder.Build().DetectNoShowBookingsAsync();

        _builder.BookingRepository.Verify(x => x.MarkNoShowAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()), Times.Never);
    }
}
