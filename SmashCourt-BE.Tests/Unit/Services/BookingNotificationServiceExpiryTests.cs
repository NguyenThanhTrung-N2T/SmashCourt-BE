using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Http;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Thông báo sau khi job hủy đơn hết hạn (gói 6A-5 Task 2, spec 7.11, 7.13):
/// mọi đơn phát <c>BookingExpired</c> kèm lưới giờ và báo người chờ slot qua dịch vụ chung (link trang chủ — B3);
/// hết hạn thanh toán không email khách (như Job-01 cũ); hết hạn duyệt gửi email "hết hạn duyệt" và báo rời hàng chờ
/// <c>REMOVED</c>/<c>EXPIRED</c>. Mỗi bước tự bắt lỗi: một bước hỏng không chặn bước sau.
/// </summary>
[Trait("Category", TestCategories.Email)]
[Trait("Catalog", "BE-IT-08")]
[Trait("Catalog", "BE-IT-21")]
public class BookingNotificationServiceExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 3, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBroadcastService> _broadcast = new();
    private readonly Mock<ISlotInterestNotificationService> _slotInterest = new();
    private readonly RecordingEmailService _email = new(TestConfigurationFactory.Create());

    private BookingNotificationService Notifications() => new(
        _broadcast.Object,
        _email,
        _slotInterest.Object,
        TestConfigurationFactory.Create(),
        new FakeTimeProvider(Now),
        Mock.Of<Microsoft.Extensions.Options.IOptionsMonitor<SmashCourt_BE.Configurations.BookingPaymentSettings>>(
            m => m.CurrentValue == new SmashCourt_BE.Configurations.BookingPaymentSettings()),
        NullLogger<BookingNotificationService>.Instance);

    private static Booking ExpiredBooking(string? email = "khach@test.local", string fullName = "Nguyễn Văn An")
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CANCELLED, bookingDate: new DateOnly(2026, 11, 20));
        booking.BookingCode = "BK-20261118-000123";
        booking.CancelSource = CancelSourceEnum.SYSTEM;
        booking.Customer = new User { Id = booking.CustomerId!.Value, Email = email!, FullName = fullName };
        booking.Branch = new Branch { Id = booking.BranchId, Name = "SmashCourt Thủ Đức", Address = "1 Võ Văn Ngân", Phone = "0281234567" };
        booking.BookingCourts.Add(new BookingCourt
        {
            CourtId = Guid.NewGuid(), Court = new Court { Name = "Sân 3" }, Date = new DateOnly(2026, 11, 20),
            StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0)
        });
        booking.Invoice = new Invoice { FinalTotal = 228_000m, PaymentStatus = InvoicePaymentStatus.EXPIRED };
        return booking;
    }

    private List<(string Event, BookingNotificationDto Dto, bool IncludeTimeGrid)> CaptureBookingEvents()
    {
        var events = new List<(string, BookingNotificationDto, bool)>();
        _broadcast.Setup(b => b.BroadcastBookingEventAsync(It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .Callback<string, BookingNotificationDto, Booking, bool>((e, dto, _, grid) => events.Add((e, dto, grid)))
            .Returns(Task.CompletedTask);
        return events;
    }

    // ── Hết hạn thanh toán (Job-01) ───────────────────────────────────────

    [Fact]
    public async Task NotifyExpired_PaymentExpiry_PublishesBookingExpiredWithTimeGridAndOldMessage()
    {
        var events = CaptureBookingEvents();
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING);

        var (name, dto, grid) = Assert.Single(events);
        Assert.Equal(SignalREvents.BookingExpired, name);
        Assert.True(grid);
        Assert.Equal("Booking #BK-20261118-000123 của Nguyễn Văn An đã bị hủy tự động do hết hạn thanh toán.", dto.Message);
        Assert.Equal(booking.Id, dto.BookingId);
        Assert.Equal(booking.BranchId, dto.BranchId);
        Assert.Equal(nameof(BookingStatus.CANCELLED), dto.Status);
        Assert.Equal(booking.BookingCourts.Single().CourtId.ToString(), Assert.Single(dto.CourtIds));
        Assert.Equal(Now.UtcDateTime, dto.Timestamp);
    }

    [Fact]
    public async Task NotifyExpired_PaymentExpiry_NotifiesSlotWaitersThroughSharedService()
    {
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING);

        _slotInterest.Verify(s => s.NotifyReleasedSlotsAsync(booking), Times.Once);
    }

    [Fact]
    public async Task NotifyExpired_PaymentExpiry_SendsNoCustomerEmailAndNoQueueEvent()
    {
        await Notifications().NotifyExpiredAsync(ExpiredBooking(), BookingStatus.PENDING);

        Assert.Empty(_email.Sent);
        _broadcast.Verify(b => b.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()), Times.Never);
    }

    // ── Hết hạn duyệt ─────────────────────────────────────────────────────

    [Fact]
    public async Task NotifyExpired_ApprovalExpiry_PublishesBookingExpiredWithApprovalMessage()
    {
        var events = CaptureBookingEvents();

        await Notifications().NotifyExpiredAsync(ExpiredBooking(), BookingStatus.PENDING_APPROVAL);

        var (name, dto, grid) = Assert.Single(events);
        Assert.Equal(SignalREvents.BookingExpired, name);
        Assert.True(grid);
        Assert.Equal("Booking #BK-20261118-000123 của Nguyễn Văn An đã bị hủy tự động do chi nhánh chưa duyệt kịp.", dto.Message);
    }

    [Fact]
    public async Task NotifyExpired_ApprovalExpiry_SendsExactlyOneApprovalExpiredEmail()
    {
        await Notifications().NotifyExpiredAsync(ExpiredBooking(), BookingStatus.PENDING_APPROVAL);

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("khach@test.local", sent.To);
        Assert.Equal("Đơn BK-20261118-000123 đã hết hạn duyệt - SmashCourt", sent.Subject);
        Assert.Contains("chưa kịp xác nhận", sent.Body);
    }

    [Fact]
    public async Task NotifyExpired_ApprovalExpiry_RemovesFromQueueWithExpiredReason()
    {
        PendingApprovalQueueChangedNotificationDto? queued = null;
        _broadcast.Setup(b => b.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()))
            .Callback<PendingApprovalQueueChangedNotificationDto>(dto => queued = dto)
            .Returns(Task.CompletedTask);
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING_APPROVAL);

        Assert.NotNull(queued);
        Assert.Equal(SignalREvents.QueueChangeRemoved, queued!.Change);
        Assert.Equal(SignalREvents.QueueReasonExpired, queued.Reason);
        Assert.Equal("EXPIRED", queued.Reason);
        Assert.Equal(booking.Id, queued.BookingId);
        Assert.Equal(booking.BranchId, queued.BranchId);
        Assert.Equal(Now.UtcDateTime, queued.Timestamp);
    }

    [Fact]
    public async Task NotifyExpired_ApprovalExpiry_NotifiesSlotWaiters()
    {
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING_APPROVAL);

        _slotInterest.Verify(s => s.NotifyReleasedSlotsAsync(booking), Times.Once);
    }

    [Fact]
    public async Task NotifyExpired_ApprovalExpiry_CustomerWithoutEmail_SkipsEmailButKeepsOtherSteps()
    {
        var events = CaptureBookingEvents();
        var booking = ExpiredBooking(email: "");

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING_APPROVAL);

        Assert.Empty(_email.Sent);
        Assert.Single(events);
        _slotInterest.Verify(s => s.NotifyReleasedSlotsAsync(booking), Times.Once);
        _broadcast.Verify(b => b.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()), Times.Once);
    }

    // ── Một bước lỗi không chặn bước sau ──────────────────────────────────

    [Fact]
    public async Task NotifyExpired_BookingEventFails_StillNotifiesQueueSlotWaitersAndEmail()
    {
        _broadcast.Setup(b => b.BroadcastBookingEventAsync(It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING_APPROVAL);

        _broadcast.Verify(b => b.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()), Times.Once);
        _slotInterest.Verify(s => s.NotifyReleasedSlotsAsync(booking), Times.Once);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task NotifyExpired_SlotWaitersFail_StillSendsEmail()
    {
        _slotInterest.Setup(s => s.NotifyReleasedSlotsAsync(It.IsAny<Booking>())).ThrowsAsync(new InvalidOperationException("DB hỏng"));

        await Notifications().NotifyExpiredAsync(ExpiredBooking(), BookingStatus.PENDING_APPROVAL);

        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task NotifyExpired_EmailFails_DoesNotThrow()
    {
        _email.FailOnSend = true;

        var act = () => Notifications().NotifyExpiredAsync(ExpiredBooking(), BookingStatus.PENDING_APPROVAL);

        var ex = await Record.ExceptionAsync(act);
        Assert.Null(ex);
    }

    [Fact]
    public async Task NotifyExpired_QueueEventFails_StillNotifiesSlotWaiters()
    {
        _broadcast.Setup(b => b.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));
        var booking = ExpiredBooking();

        await Notifications().NotifyExpiredAsync(booking, BookingStatus.PENDING_APPROVAL);

        _slotInterest.Verify(s => s.NotifyReleasedSlotsAsync(booking), Times.Once);
        Assert.Single(_email.Sent);
    }
}
