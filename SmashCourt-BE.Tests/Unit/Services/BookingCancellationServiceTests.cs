using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Dịch vụ hủy chung: ba đường hủy (nhân viên, khách trong app, link email) và xem trước số hoàn. Hoàn tiền tính trên số đã thu,
/// theo giờ VN quy đổi UTC; lượt khuyến mãi chỉ trả cho đơn đã được tính lượt; lệnh hủy thua câu cập nhật có điều kiện thì không ghi gì.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingCancellationServiceTests
{
    private const string RawToken = "raw-cancel-token";
    private static readonly DateTime Now = BookingCancellationServiceTestBuilder.DefaultNow.UtcDateTime;

    // Giờ chơi 18:00 giờ VN; mặc định ngày 22/11 (49 giờ nữa, mốc hoàn 100 %)
    private static readonly TimeOnly PlayStart = new(18, 0);
    private static readonly DateOnly FarDate = new(2026, 11, 22);

    private sealed record Order(Booking Booking, Court Court, Guid PromotionId);

    /// <summary>Đơn 200.000 đ một sân, có khuyến mãi; khách có email.</summary>
    private static Order CreateOrder(
        BookingStatus status, PaymentTiming timing = PaymentTiming.PREPAID, DateOnly? date = null, TimeOnly? start = null,
        params Payment[] payments)
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Chi nhánh 1", Address = "1 Lê Lợi", Phone = "0281234567" };
        var customer = TestDataFactory.CreateUser(email: "khach@example.com", fullName: "Nguyễn Văn A");
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(customer.Id, branch.Id, status, bookingDate: date ?? FarDate);
        booking.Customer = customer;
        booking.Branch = branch;
        booking.BookingCode = "BK-001";
        var playStart = start ?? PlayStart;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Court = court, Date = booking.BookingDate,
            StartTime = playStart, EndTime = playStart.AddHours(1), IsActive = true
        });
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, 200_000m, paymentTiming: timing, payments: payments);
        var promotionId = Guid.NewGuid();
        booking.BookingPromotion = new BookingPromotion { BookingId = booking.Id, PromotionId = promotionId };
        if (status == BookingStatus.PENDING_APPROVAL)
            booking.ApprovalExpiresAt = Now.AddMinutes(20);
        return new Order(booking, court, promotionId);
    }

    private static Payment Paid(decimal amount) => TestDataFactory.CreatePayment(amount: amount, status: PaymentTxStatus.SUCCESS,
        method: PaymentTxMethod.VNPAY);

    private static Payment Waiting() => TestDataFactory.CreatePayment(amount: 60_000m, status: PaymentTxStatus.PENDING,
        method: PaymentTxMethod.VNPAY);

    private static BookingCancellationServiceTestBuilder BuilderFor(Order order)
    {
        var builder = new BookingCancellationServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(order.Booking.Id)).ReturnsAsync(order.Booking);
        return builder;
    }

    private static BookingCancellationServiceTestBuilder BuilderForToken(Order order, DateTime? expiresAt = null)
    {
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash(RawToken);
        order.Booking.CancelTokenExpiresAt = expiresAt ?? Now.AddHours(24);
        var builder = new BookingCancellationServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByCancelTokenAsync(CancelTokenHelper.Hash(RawToken))).ReturnsAsync(order.Booking);
        return builder;
    }

    // anyActor: không kiểm người hủy (ca chỉ quan tâm trạng thái và tiền)
    private static void VerifyCancelled(
        BookingCancellationServiceTestBuilder builder, Order order, BookingStatus from, BookingStatus to, CancelSourceEnum source,
        BookingCancelReason reason, Guid? cancelledBy, bool anyActor = false)
    {
        if (anyActor)
            builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
                order.Booking.Id,
                It.Is<IReadOnlyCollection<BookingStatus>>(s => s.SequenceEqual(new[] { from })),
                to, source, reason, It.IsAny<Guid?>(), Now), Times.Once);
        else
            builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
                order.Booking.Id,
                It.Is<IReadOnlyCollection<BookingStatus>>(s => s.SequenceEqual(new[] { from })),
                to, source, reason, cancelledBy, Now), Times.Once);
    }

    private static void VerifyNothingWritten(BookingCancellationServiceTestBuilder builder)
    {
        builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
            It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
        builder.BookingRepository.Verify(x => x.TryConsumeTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    private static void VerifyPromotionReleased(BookingCancellationServiceTestBuilder builder, Order order, bool released) =>
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(order.PromotionId), released ? Times.Once() : Times.Never());

    // ───────────────────────── Nhân viên: ma trận trạng thái × tiền ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task Staff_PendingOnlineOrder_CancelsWithoutRefundAndKeepsPromotionUse()
    {
        // Đơn online chưa trả: lượt khuyến mãi chưa từng được tính, link thanh toán đang mở bị vô hiệu
        var order = CreateOrder(BookingStatus.PENDING, payments: Waiting());
        var builder = BuilderFor(order);
        var staffId = Guid.NewGuid();

        await builder.Build().CancelByStaffAsync(order.Booking.Id, staffId, nameof(UserRole.OWNER));

        VerifyCancelled(builder, order, BookingStatus.PENDING, BookingStatus.CANCELLED, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, staffId);
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(order.Booking.Invoice!.Id, null, Now), Times.Once);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        VerifyPromotionReleased(builder, order, released: false);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(order.Booking.Id, false), Times.Once);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(order.Booking.Id), Times.Once);
        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(
            It.Is<List<Guid>>(ids => ids.SequenceEqual(new[] { order.Court.Id })), CourtStatus.AVAILABLE, Now), Times.Once);
        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-23")]
    public async Task Staff_PendingApprovalOrder_CancelsAndLeavesTheQueue()
    {
        var order = CreateOrder(BookingStatus.PENDING_APPROVAL);
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        VerifyCancelled(builder, order, BookingStatus.PENDING_APPROVAL, BookingStatus.CANCELLED, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, null, anyActor: true);
        VerifyPromotionReleased(builder, order, released: false);
        builder.Broadcast.Verify(x => x.BroadcastPendingApprovalQueueChangedAsync(It.Is<PendingApprovalQueueChangedNotificationDto>(n =>
            n.BookingId == order.Booking.Id && n.Change == SignalREvents.QueueChangeRemoved
            && n.Reason == SignalREvents.QueueReasonCancelled)), Times.Once);
    }

    [Fact]
    public async Task Staff_PayAtCounterOrder_ReleasesThePromotionUse()
    {
        // Thu tại quầy (đơn tại quầy trả sau, đặt online không qua VNPay, duyệt 0 %): lượt đã tính lúc xác nhận
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        VerifyCancelled(builder, order, BookingStatus.CONFIRMED, BookingStatus.CANCELLED, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, null, anyActor: true);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        VerifyPromotionReleased(builder, order, released: true);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task Staff_DepositPaidOrder_RefundsOnTheDepositNotTheTotal()
    {
        // Cọc 30 % (60.000 / 200.000), hủy trước 49 giờ: hoàn 100 % của 60.000; mã cũ hoàn 200.000
        var deposit = Paid(60_000m);
        var order = CreateOrder(BookingStatus.PAID_ONLINE, payments: deposit);
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        VerifyCancelled(builder, order, BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED_PENDING_REFUND, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, null, anyActor: true);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(r =>
            r.PaymentId == deposit.Id && r.Amount == 60_000m && r.RefundPercent == 100m && r.Status == RefundStatus.PENDING
            && r.CreatedAt == Now)), Times.Once);
        VerifyPromotionReleased(builder, order, released: true);
        Assert.Contains(builder.Email.To("khach@example.com"), m => m.Body.Contains(60_000m.ToString("N0")));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task Staff_FullyPaidOrder_RefundsByPolicyTierInVietnamTime()
    {
        // 18:00 giờ VN ngày 21/11 = còn 25 giờ ⇒ mốc 24 giờ; ngày hôm sau sát giờ ⇒ mốc 12 giờ
        var payment = Paid(200_000m);
        var order = CreateOrder(BookingStatus.PAID_ONLINE, date: new DateOnly(2026, 11, 21), start: new TimeOnly(4, 0), payments: payment);
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        // 04:00 giờ VN 21/11 = 21:00 UTC 20/11, còn 11 giờ ⇒ mốc 2 giờ (20 %); mã cũ dư 7 giờ ⇒ mốc 12 giờ (50 %)
        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(r =>
            r.PaymentId == payment.Id && r.Amount == 40_000m && r.RefundPercent == 20m)), Times.Once);
    }

    [Fact]
    public async Task Staff_PaidOrderTooCloseToStart_CancelsWithoutRefund()
    {
        // Còn 1 giờ: không mốc nào áp dụng ⇒ CANCELLED, không tạo Refund
        var order = CreateOrder(BookingStatus.CONFIRMED, date: new DateOnly(2026, 11, 20), start: new TimeOnly(18, 0), payments: Paid(200_000m));
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        VerifyCancelled(builder, order, BookingStatus.CONFIRMED, BookingStatus.CANCELLED, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, null, anyActor: true);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        VerifyPromotionReleased(builder, order, released: true);
    }

    [Fact]
    public async Task Staff_TwoCollectedPayments_OneRefundPerPayment()
    {
        var first = Paid(60_000m);
        var second = Paid(60_000m);
        var order = CreateOrder(BookingStatus.PAID_ONLINE, payments: [first, second]);
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(r => r.PaymentId == first.Id && r.Amount == 60_000m)), Times.Once);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(r => r.PaymentId == second.Id && r.Amount == 60_000m)), Times.Once);
    }

    [Fact]
    public async Task Staff_AfterStartTime_CanStillCancel()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID, new DateOnly(2026, 11, 20), new TimeOnly(16, 0));
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task Staff_StatusNotCancellable_ReturnsBadRequestWithoutWriting(BookingStatus status)
    {
        var order = CreateOrder(status);
        var builder = BuilderFor(order);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER)));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "không thể hủy");
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Staff_OtherBranch_Forbidden()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderFor(order);
        builder.UserBranchRepository.Setup(x => x.IsUserInBranchAsync(It.IsAny<Guid>(), order.Booking.BranchId)).ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.STAFF)));

        error.ShouldBeAppException(403, ErrorCodes.Forbidden);
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Staff_NotFound()
    {
        var builder = new BookingCancellationServiceTestBuilder();

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByStaffAsync(Guid.NewGuid(), Guid.NewGuid(), nameof(UserRole.OWNER)));

        error.ShouldBeAppException(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Staff_StatusChangedBeforeTheWrite_ConflictAndNothingElseWritten()
    {
        // Nhân viên đọc PENDING, IPN vừa chuyển PAID_ONLINE trước khi nhân viên ghi: không được ghi đè
        var order = CreateOrder(BookingStatus.PENDING, payments: Waiting());
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.CancelWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
                It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER)));

        error.ShouldBeAppException(409, ErrorCodes.Conflict);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        builder.PaymentRepository.Verify(x => x.FailPendingByInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()), Times.Never);
        Assert.Empty(builder.Email.Sent);
    }

    [Fact]
    public async Task Staff_CourtStillHeldByAnotherBooking_StaysAsIs()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.GetActiveByCourtAndDateAsync(order.Court.Id, order.Booking.BookingDate))
            .ReturnsAsync([new BookingCourt { BookingId = Guid.NewGuid(), CourtId = order.Court.Id }]);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(It.IsAny<List<Guid>>(), It.IsAny<CourtStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Theory]
    [InlineData(CourtStatus.SUSPENDED)]
    [InlineData(CourtStatus.INACTIVE)]
    [InlineData(CourtStatus.IN_USE)]
    public async Task Staff_CourtInSpecialStatus_StaysAsIs(CourtStatus courtStatus)
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        order.Court.Status = courtStatus;
        var builder = BuilderFor(order);

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(It.IsAny<List<Guid>>(), It.IsAny<CourtStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Staff_NotificationFailure_DoesNotUndoTheCancellation()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        var builder = BuilderFor(order);
        builder.Broadcast.Setup(x => x.BroadcastBookingEventAsync(
                It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));
        builder.Email.FailOnSend = true;

        await builder.Build().CancelByStaffAsync(order.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
        Assert.Contains(builder.NotificationLogger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    // ───────────────────────── Khách trong app ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-23")]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    public async Task Customer_UnpaidOrder_CancelsAsCustomer(BookingStatus status)
    {
        var order = CreateOrder(status);
        var builder = BuilderFor(order);

        await builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value);

        VerifyCancelled(builder, order, status, BookingStatus.CANCELLED, CancelSourceEnum.CUSTOMER,
            BookingCancelReason.CUSTOMER_CANCELLED, null);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        VerifyPromotionReleased(builder, order, released: false);
    }

    [Fact]
    public async Task Customer_PaidOrder_RefundsAndInvalidatesTheEmailLink()
    {
        var order = CreateOrder(BookingStatus.PAID_ONLINE, payments: Paid(60_000m));
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash(RawToken);
        var builder = BuilderFor(order);

        await builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value);

        VerifyCancelled(builder, order, BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED_PENDING_REFUND, CancelSourceEnum.CUSTOMER,
            BookingCancelReason.CUSTOMER_CANCELLED, null);
        builder.BookingRepository.Verify(x => x.TryConsumeTokenAsync(order.Booking.Id, CancelTokenHelper.Hash(RawToken), Now), Times.Once);
    }

    [Fact]
    public async Task Customer_LinkAlreadyUsedByCheckIn_DoesNotBlockTheInAppCancel()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash(RawToken);
        var builder = BuilderFor(order);
        builder.BookingRepository.Setup(x => x.TryConsumeTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        await builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value);

        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task Customer_AtTheStartTime_BookingAlreadyStarted()
    {
        // 17:00 giờ VN, đơn bắt đầu 17:00 hôm nay
        var order = CreateOrder(BookingStatus.PAID_ONLINE, date: new DateOnly(2026, 11, 20), start: new TimeOnly(17, 0), payments: Paid(200_000m));
        var builder = BuilderFor(order);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value));

        error.ShouldBeAppException(400, ErrorCodes.BookingAlreadyStarted);
        VerifyNothingWritten(builder);
        Assert.Equal(BookingStatus.PAID_ONLINE, order.Booking.Status);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task Customer_OneMinuteBeforeStart_CanCancel()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID, new DateOnly(2026, 11, 20), new TimeOnly(17, 1));
        var builder = BuilderFor(order);

        await builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value);

        Assert.Equal(BookingStatus.CANCELLED, order.Booking.Status);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task Customer_AlreadyCancelled_IsIdempotent(BookingStatus status)
    {
        var order = CreateOrder(status);
        var builder = BuilderFor(order);

        await builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value);

        VerifyNothingWritten(builder);
    }

    [Theory]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    public async Task Customer_StatusNotCancellable_BadRequest(BookingStatus status)
    {
        var order = CreateOrder(status);
        var builder = BuilderFor(order);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "không thể hủy");
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Customer_NotTheOwner_Forbidden()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderFor(order);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByCustomerAsync(order.Booking.Id, Guid.NewGuid()));

        error.ShouldBeAppException(403, ErrorCodes.Forbidden);
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Customer_LockedAccount_Forbidden()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        order.Booking.Customer!.Status = UserStatus.LOCKED;
        var builder = BuilderFor(order);

        var error = await Assert.ThrowsAsync<AppException>(
            () => builder.Build().CancelByCustomerAsync(order.Booking.Id, order.Booking.CustomerId!.Value));

        error.ShouldBeAppException(403, ErrorCodes.AccountLocked);
    }

    // ───────────────────────── Link hủy trong email ─────────────────────────

    [Fact]
    public async Task Link_PayAtCounterOrder_CancelsAndReleasesThePromotionUse()
    {
        // Trước đây hủy bằng link không bao giờ trả lượt khuyến mãi
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        var builder = BuilderForToken(order);

        await builder.Build().CancelByTokenAsync(RawToken);

        VerifyCancelled(builder, order, BookingStatus.CONFIRMED, BookingStatus.CANCELLED, CancelSourceEnum.LINK,
            BookingCancelReason.CUSTOMER_CANCELLED, null);
        builder.BookingRepository.Verify(x => x.TryConsumeTokenAsync(order.Booking.Id, CancelTokenHelper.Hash(RawToken), Now), Times.Once);
        VerifyPromotionReleased(builder, order, released: true);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingCancelled,
            It.Is<BookingNotificationDto>(n => n.Message.Contains("link email")), order.Booking, true), Times.Once);
    }

    [Fact]
    public async Task Link_Expired_FailsWithoutUsingTheToken()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderForToken(order, expiresAt: Now.AddSeconds(-1));

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "hết hạn");
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Link_StatusNotCancellable_FailsWithoutUsingTheToken()
    {
        // Mã cũ tiêu token trước khi kiểm: link chết mà đơn không bị hủy
        var order = CreateOrder(BookingStatus.IN_PROGRESS);
        var builder = BuilderForToken(order);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "không thể hủy");
        VerifyNothingWritten(builder);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task Link_AfterStart_BookingAlreadyStartedWithoutUsingTheToken()
    {
        // Hạn link vốn không vượt giờ bắt đầu; kiểm giờ bắt đầu là lớp thứ hai
        var order = CreateOrder(BookingStatus.PAID_ONLINE, date: new DateOnly(2026, 11, 20), start: new TimeOnly(16, 0));
        var builder = BuilderForToken(order, expiresAt: Now.AddHours(1));

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BookingAlreadyStarted);
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Link_AlreadyUsed_FailsBeforeAnyWrite()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderForToken(order);
        order.Booking.CancelTokenUsedAt = Now.AddMinutes(-5);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "đã được sử dụng");
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task Link_TwoClicksAtOnce_LoserGetsAlreadyUsedAndWritesNothing()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderForToken(order);
        builder.BookingRepository.Setup(x => x.TryConsumeTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "đã được sử dụng");
        builder.BookingRepository.Verify(x => x.CancelWithStatusCheckAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
            It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Link_UnknownToken_NotFound()
    {
        var builder = new BookingCancellationServiceTestBuilder();

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().CancelByTokenAsync(RawToken));

        error.ShouldBeAppException(404, ErrorCodes.NotFound);
        builder.BookingRepository.Verify(x => x.GetByCancelTokenAsync(CancelTokenHelper.Hash(RawToken)), Times.Once);
    }

    [Fact]
    public async Task Link_AlreadyCancelled_IsIdempotent()
    {
        var order = CreateOrder(BookingStatus.CANCELLED);
        var builder = BuilderForToken(order);

        await builder.Build().CancelByTokenAsync(RawToken);

        VerifyNothingWritten(builder);
    }

    // ───────────────────────── Xem trước ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task Preview_DepositPaidOrder_ShowsTheSameAmountAsTheCancel()
    {
        var order = CreateOrder(BookingStatus.PAID_ONLINE, payments: Paid(60_000m));
        var builder = BuilderForToken(order);

        var info = await builder.Build().GetCancelInfoAsync(RawToken);

        Assert.Equal(100m, info.RefundPercent);
        Assert.Equal(60_000m, info.RefundAmount);
        Assert.Equal(nameof(BookingStatus.PAID_ONLINE), info.Status);
    }

    [Fact]
    public async Task Preview_PayAtCounterOrder_ShowsZeroAmount()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED, PaymentTiming.POSTPAID);
        var builder = BuilderForToken(order);

        var info = await builder.Build().GetCancelInfoAsync(RawToken);

        Assert.Equal(0m, info.RefundAmount);
    }

    [Fact]
    public async Task Preview_ExpiredLink_FailsWithoutLoadingPolicies()
    {
        var order = CreateOrder(BookingStatus.CONFIRMED);
        var builder = BuilderForToken(order, expiresAt: Now.AddMinutes(-1));

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().GetCancelInfoAsync(RawToken));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "hết hạn");
        builder.CancelPolicyRepository.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task Preview_UnknownToken_LooksUpByTheSharedHash()
    {
        var builder = new BookingCancellationServiceTestBuilder();

        await Assert.ThrowsAsync<AppException>(() => builder.Build().GetCancelInfoAsync(RawToken));

        builder.BookingRepository.Verify(x => x.GetByCancelTokenAsync(CancelTokenHelper.Hash(RawToken)), Times.Once);
    }
}
