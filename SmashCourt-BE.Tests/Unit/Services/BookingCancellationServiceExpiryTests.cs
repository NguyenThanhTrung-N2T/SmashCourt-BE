using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Hủy do hết hạn của job (gói 6A-5 Task 1, spec 7.11, 4.4.1): khóa booking ⇒ kiểm lại "vẫn quá hạn" dưới khóa (B2) ⇒ lõi hủy có điều kiện
/// (<c>SYSTEM</c>, <c>cancelledBy = null</c>) ⇒ nhả sân, xóa slot lock ⇒ Payment chờ ⇒ <c>FAILED</c> (C7) ⇒ hóa đơn <c>EXPIRED</c> ⇒ commit ⇒ thông báo.
/// Thua ở bất kỳ điều kiện nào ⇒ <c>false</c>, không ghi gì, không thông báo. <c>SYSTEM_CLEANUP</c> chỉ khi chưa có mức cọc và chưa có Payment (B1).
/// Không tạo Refund, không trả lượt khuyến mãi (đơn chờ chưa từng được tính lượt).
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingCancellationServiceExpiryTests
{
    private static readonly DateTime Now = BookingCancellationServiceTestBuilder.DefaultNow.UtcDateTime;

    private sealed record Order(Booking Booking, Court Court);

    /// <summary>Đơn online một sân, chơi ngày 22/11 18:00 giờ VN; hóa đơn UNPAID quá hạn 1 phút.</summary>
    private static Order CreateOrder(BookingStatus status, DepositSource? depositSource, params Payment[] payments)
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Chi nhánh 1", Address = "1 Lê Lợi", Phone = "0281234567" };
        var customer = TestDataFactory.CreateUser(email: "khach@example.com", fullName: "Nguyễn Văn A");
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(customer.Id, branch.Id, status, bookingDate: new DateOnly(2026, 11, 22));
        booking.Customer = customer;
        booking.Branch = branch;
        booking.BookingCode = "BK-001";
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Court = court, Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(19, 0), IsActive = true
        });
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, 200_000m, paymentTiming: PaymentTiming.PREPAID,
            expiresAt: status == BookingStatus.PENDING ? Now.AddMinutes(-1) : null, payments: payments);
        booking.Invoice.DepositSource = depositSource;
        booking.BookingPromotion = new BookingPromotion { BookingId = booking.Id, PromotionId = Guid.NewGuid() };
        if (status == BookingStatus.PENDING_APPROVAL)
            booking.ApprovalExpiresAt = Now.AddMinutes(-1);
        return new Order(booking, court);
    }

    private static Payment PaymentWith(PaymentTxStatus status) =>
        TestDataFactory.CreatePayment(amount: 200_000m, status: status, method: PaymentTxMethod.VNPAY);

    /// <summary>Mặc định: đơn được nạp, kiểm hạn dưới khóa thấy vẫn quá hạn, lõi hủy thắng (builder).</summary>
    private static BookingCancellationServiceTestBuilder BuilderFor(Order order, bool windowOver = true)
    {
        var builder = new BookingCancellationServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(order.Booking.Id)).ReturnsAsync(order.Booking);
        builder.BookingRepository.Setup(x => x.IsPaymentWindowOverAsync(order.Booking.Id, Now)).ReturnsAsync(windowOver);
        builder.BookingRepository.Setup(x => x.IsApprovalWindowOverAsync(order.Booking.Id, Now)).ReturnsAsync(windowOver);
        return builder;
    }

    private static void VerifyExpiredAs(BookingCancellationServiceTestBuilder builder, Order order, BookingStatus from, BookingCancelReason reason) =>
        builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
            order.Booking.Id,
            It.Is<IReadOnlyCollection<BookingStatus>>(s => s.Count == 1 && s.Contains(from)),
            BookingStatus.CANCELLED, CancelSourceEnum.SYSTEM, reason, null, Now), Times.Once);

    private static void VerifyNothingWrittenAfterCheck(BookingCancellationServiceTestBuilder builder)
    {
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(It.IsAny<List<Guid>>(), It.IsAny<CourtStatus>(), It.IsAny<DateTime>()), Times.Never);
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<InvoicePaymentStatus>(), It.IsAny<DateTime>()), Times.Never);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
    }

    private static void VerifyNoNotification(BookingCancellationServiceTestBuilder builder)
    {
        builder.Broadcast.VerifyNoOtherCalls();
        Assert.Empty(builder.Email.Sent);
    }

    // ── B1: lý do hủy của đơn hết hạn thanh toán ──────────────────────────

    public static TheoryData<DepositSource?, PaymentTxStatus[], BookingCancelReason> ReasonCases => new()
    {
        // Khách mở link rồi bỏ
        { DepositSource.TADSS, [PaymentTxStatus.PENDING], BookingCancelReason.PAYMENT_EXPIRED },
        // Thẻ lỗi rồi bỏ
        { DepositSource.FULL_PAYMENT_FALLBACK, [PaymentTxStatus.FAILED], BookingCancelReason.PAYMENT_EXPIRED },
        // Quản lý đã duyệt, khách chưa bấm "Thanh toán cọc" (Payment chỉ tạo khi khách bấm, P6-10)
        { DepositSource.MANAGER, [], BookingCancelReason.PAYMENT_EXPIRED },
        // Thanh toán lại nhiều lần
        { DepositSource.TADSS, [PaymentTxStatus.FAILED, PaymentTxStatus.FAILED, PaymentTxStatus.PENDING], BookingCancelReason.PAYMENT_EXPIRED },
        // BE dừng giữa hai transaction: chưa có mức cọc, chưa có Payment
        { null, [], BookingCancelReason.SYSTEM_CLEANUP },
        // Dữ liệu trước Phase 6: chưa có mức cọc nhưng có Payment
        { null, [PaymentTxStatus.FAILED], BookingCancelReason.PAYMENT_EXPIRED },
    };

    [Theory]
    [MemberData(nameof(ReasonCases))]
    public async Task ExpireUnpaid_ChoosesReasonFromDepositSourceAndPayments(
        DepositSource? depositSource, PaymentTxStatus[] payments, BookingCancelReason expected)
    {
        var order = CreateOrder(BookingStatus.PENDING, depositSource, payments.Select(PaymentWith).ToArray());
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        VerifyExpiredAs(builder, order, BookingStatus.PENDING, expected);
        Assert.Equal(expected, order.Booking.CancelReason);
    }

    // ── C7: trạng thái sau khi job thắng ──────────────────────────────────

    [Fact]
    public async Task ExpireUnpaid_Wins_WritesEveryRowOfTheStateTable()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.FAILED), PaymentWith(PaymentTxStatus.PENDING));
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        // Booking: CANCELLED, SYSTEM, PAYMENT_EXPIRED, không có người hủy
        VerifyExpiredAs(builder, order, BookingStatus.PENDING, BookingCancelReason.PAYMENT_EXPIRED);
        // Sân: booking_courts tắt, slot lock của đơn bị xóa, sân về AVAILABLE khi không đơn khác giữ
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(order.Booking.Id, false), Times.Once);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(order.Booking.Id), Times.Once);
        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(
            It.Is<List<Guid>>(ids => ids.SequenceEqual(new[] { order.Court.Id })), CourtStatus.AVAILABLE, Now), Times.Once);
        // Payment: mọi Payment PENDING ⇒ FAILED
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(order.Booking.Invoice!.Id, null, Now), Times.Once);
        // Hóa đơn: UNPAID ⇒ EXPIRED
        builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(order.Booking.Invoice!.Id, InvoicePaymentStatus.EXPIRED, Now), Times.Once);
        // Refund, lượt khuyến mãi: không tạo, không đổi
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ExpireUnpaid_Wins_UpdatesTheLoadedBookingForNotifications()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS);

        Assert.True(await BuilderFor(order).Build().ExpireUnpaidAsync(order.Booking.Id));

        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
        Assert.Equal(CancelSourceEnum.SYSTEM, order.Booking.CancelSource);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, order.Booking.CancelReason);
        Assert.Equal(Now, order.Booking.CancelledAt);
        Assert.Null(order.Booking.CancelledBy);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, order.Booking.Invoice!.PaymentStatus);
    }

    [Fact]
    public async Task ExpireUnpaid_CourtHeldByAnotherOrder_IsNotReleased()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS);
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.GetActiveByCourtAndDateAsync(order.Court.Id, order.Booking.BookingDate))
            .ReturnsAsync([new BookingCourt { BookingId = Guid.NewGuid(), CourtId = order.Court.Id }]);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(It.IsAny<List<Guid>>(), It.IsAny<CourtStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task ExpireUnpaid_RunsLockThenCheckThenCoreThenPaymentThenInvoice()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.PENDING));
        var builder = BuilderFor(order);
        var calls = new List<string>();
        builder.BookingRepository.Setup(x => x.LockAsync(order.Booking.Id)).Callback(() => calls.Add("lock")).Returns(Task.CompletedTask);
        builder.BookingRepository.Setup(x => x.IsPaymentWindowOverAsync(order.Booking.Id, Now)).Callback(() => calls.Add("check")).ReturnsAsync(true);
        builder.BookingRepository.Setup(x => x.CancelWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
                It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .Callback(() => calls.Add("cancel")).ReturnsAsync(1);
        builder.PaymentRepository.Setup(x => x.FailPendingByInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .Callback(() => calls.Add("payment")).ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.SetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<InvoicePaymentStatus>(), It.IsAny<DateTime>()))
            .Callback(() => calls.Add("invoice")).Returns(Task.CompletedTask);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        Assert.Equal(["lock", "check", "cancel", "payment", "invoice"], calls);
    }

    // ── C4, B2: kiểm lại hạn dưới khóa ────────────────────────────────────

    [Fact]
    public async Task ExpireUnpaid_ChecksTheWindowWithTheCurrentClock()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS);
        var builder = BuilderFor(order);

        await builder.Build().ExpireUnpaidAsync(order.Booking.Id);

        // Mốc so sánh là "bây giờ" của TimeProvider; luật "<= now" (C4) nằm ở repository, kiểm trên DB thật
        builder.BookingRepository.Verify(x => x.IsPaymentWindowOverAsync(order.Booking.Id, Now), Times.Once);
        builder.BookingRepository.Verify(x => x.IsApprovalWindowOverAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task ExpireUnpaid_WindowJustExtendedUnderLock_ReturnsFalseAndWritesNothing()
    {
        // B2: khách vừa bấm "Thanh toán lại", hạn mới đã commit trước khi job khóa được đơn
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.PENDING));
        var builder = BuilderFor(order, windowOver: false);

        Assert.False(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.BookingRepository.Verify(x => x.LockAsync(order.Booking.Id), Times.Once);
        builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
            It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
        VerifyNothingWrittenAfterCheck(builder);
        VerifyNoNotification(builder);
        Assert.Equal(BookingStatus.PENDING, order.Booking.Status);
    }

    [Fact]
    public async Task ExpireUnpaid_CoreCancelLoses_ReturnsFalseAndWritesNothingElse()
    {
        // IPN vừa chuyển đơn sang PAID_ONLINE giữa lúc kiểm hạn và lúc hủy (không thể xảy ra khi đã khóa, nhưng lõi hủy vẫn là chốt chặn)
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.PENDING));
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.CancelWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
                It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        Assert.False(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        VerifyNothingWrittenAfterCheck(builder);
        VerifyNoNotification(builder);
        Assert.Equal(BookingStatus.PENDING, order.Booking.Status);
    }

    // ── Đọc ra đơn không còn chờ ──────────────────────────────────────────

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    public async Task ExpireUnpaid_OrderNoLongerPending_ReturnsFalseWithoutTransaction(BookingStatus status)
    {
        var order = CreateOrder(status, DepositSource.TADSS);
        var builder = BuilderFor(order);

        Assert.False(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
        VerifyNothingWrittenAfterCheck(builder);
        VerifyNoNotification(builder);
    }

    [Fact]
    public async Task ExpireUnpaid_OrderNotFound_ReturnsFalseWithoutTransaction()
    {
        var builder = new BookingCancellationServiceTestBuilder();

        Assert.False(await builder.Build().ExpireUnpaidAsync(Guid.NewGuid()));

        builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ExpireUnpaid_PendingWithSuccessfulPayment_ReturnsFalseAndLogsError()
    {
        // Không thể xảy ra: IPN đổi trạng thái đơn trong cùng transaction với Payment SUCCESS. Có thì là dữ liệu hỏng, không hủy
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.SUCCESS));
        var builder = BuilderFor(order);

        Assert.False(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
        VerifyNothingWrittenAfterCheck(builder);
        var error = Assert.Single(builder.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(order.Booking.Id.ToString(), error.Message);
    }

    // ── Hết hạn duyệt ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExpireApproval_Wins_CancelsWithApprovalExpiredAndExpiresInvoice()
    {
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL, depositSource: null);
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        VerifyExpiredAs(builder, order, BookingStatus.PENDING_APPROVAL, BookingCancelReason.APPROVAL_EXPIRED);
        builder.BookingRepository.Verify(x => x.IsApprovalWindowOverAsync(order.Booking.Id, Now), Times.Once);
        builder.BookingRepository.Verify(x => x.IsPaymentWindowOverAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(order.Booking.Id, false), Times.Once);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(order.Booking.Id), Times.Once);
        builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(order.Booking.Invoice!.Id, InvoicePaymentStatus.EXPIRED, Now), Times.Once);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ExpireApproval_Wins_NotifiesExpiryEmailQueueAndNotCancellation()
    {
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL, depositSource: null);
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingExpired, It.IsAny<BookingNotificationDto>(), order.Booking, true), Times.Once);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingCancelled, It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastPendingApprovalQueueChangedAsync(It.Is<PendingApprovalQueueChangedNotificationDto>(
            d => d.Change == SignalREvents.QueueChangeRemoved && d.Reason == SignalREvents.QueueReasonExpired)), Times.Once);
        var email = Assert.Single(builder.Email.Sent);
        Assert.Contains("hết hạn duyệt", email.Subject);
    }

    [Fact]
    public async Task ExpireUnpaid_Wins_PublishesBookingExpiredWithoutCustomerEmail()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS);
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingExpired, It.IsAny<BookingNotificationDto>(), order.Booking, true), Times.Once);
        builder.Broadcast.Verify(x => x.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()), Times.Never);
        Assert.Empty(builder.Email.Sent);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]          // vừa được duyệt
    [InlineData(BookingStatus.CONFIRMED)]        // vừa được duyệt 0 %
    [InlineData(BookingStatus.CANCELLED)]        // vừa bị từ chối hoặc hủy
    public async Task ExpireApproval_OrderNoLongerWaitingForApproval_ReturnsFalse(BookingStatus status)
    {
        var order = CreateOrder(status, DepositSource.MANAGER);
        var builder = BuilderFor(order);

        Assert.False(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        builder.BookingRepository.Verify(x => x.LockAsync(It.IsAny<Guid>()), Times.Never);
        VerifyNothingWrittenAfterCheck(builder);
        VerifyNoNotification(builder);
    }

    [Fact]
    public async Task ExpireApproval_StillWithinWindowUnderLock_ReturnsFalseAndWritesNothing()
    {
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL, depositSource: null);
        var builder = BuilderFor(order, windowOver: false);

        Assert.False(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        VerifyNothingWrittenAfterCheck(builder);
        VerifyNoNotification(builder);
    }

    // ── Thông báo ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpireUnpaid_NotificationThrows_StillReturnsTrue()
    {
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS);
        var builder = BuilderFor(order);
        var notifications = new Mock<IBookingNotificationService>();
        notifications.Setup(x => x.NotifyExpiredAsync(It.IsAny<Booking>(), It.IsAny<BookingStatus>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));
        builder.NotificationsOverride = notifications.Object;

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        notifications.Verify(x => x.NotifyExpiredAsync(order.Booking, BookingStatus.PENDING), Times.Once);
        notifications.Verify(x => x.NotifyCancelledAsync(It.IsAny<Booking>(), It.IsAny<BookingStatus>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task ExpireApproval_PassesPendingApprovalAsPreviousStatus()
    {
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL, depositSource: null);
        var builder = BuilderFor(order);
        var notifications = new Mock<IBookingNotificationService>();
        builder.NotificationsOverride = notifications.Object;

        Assert.True(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        notifications.Verify(x => x.NotifyExpiredAsync(order.Booking, BookingStatus.PENDING_APPROVAL), Times.Once);
    }

    [Fact]
    public async Task ExpireApproval_OrderWithoutInvoice_StillCancelsAndReleasesTheCourt()
    {
        // DB thật luôn có hóa đơn; phòng thủ: không có thì bỏ bước Payment / hóa đơn, không ném
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL, depositSource: null);
        order.Booking.Invoice = null;
        var builder = BuilderFor(order);

        Assert.True(await builder.Build().ExpireApprovalAsync(order.Booking.Id));

        VerifyExpiredAs(builder, order, BookingStatus.PENDING_APPROVAL, BookingCancelReason.APPROVAL_EXPIRED);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(order.Booking.Id, false), Times.Once);
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<InvoicePaymentStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task ExpireUnpaid_WritesUnderOneClockReading()
    {
        // Mọi cột (cancelled_at, updated_at, hóa đơn, Payment) cùng một mốc dù đồng hồ trôi trong lúc chạy
        var order = CreateOrder(BookingStatus.PENDING, DepositSource.TADSS, PaymentWith(PaymentTxStatus.PENDING));
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.LockAsync(order.Booking.Id))
            .Callback(() => builder.Time.Advance(TimeSpan.FromSeconds(3))).Returns(Task.CompletedTask);

        Assert.True(await builder.Build().ExpireUnpaidAsync(order.Booking.Id));

        builder.BookingRepository.Verify(x => x.IsPaymentWindowOverAsync(order.Booking.Id, Now), Times.Once);
        VerifyExpiredAs(builder, order, BookingStatus.PENDING, BookingCancelReason.PAYMENT_EXPIRED);
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(order.Booking.Invoice!.Id, null, Now), Times.Once);
        builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(order.Booking.Invoice!.Id, InvoicePaymentStatus.EXPIRED, Now), Times.Once);
        Assert.Equal(Now, order.Booking.CancelledAt);
    }
}
