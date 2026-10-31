using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Lõi áp kết quả giao dịch, dùng chung cho mọi cổng: thành công, thất bại không hủy đơn (ân hạn), tiền về sau khi hủy (hoàn 100 %
/// tại chi nhánh), trả hai lần (ghi nhận, không đổi đơn). Chống lặp theo trạng thái Payment.
/// </summary>
[Trait("Category", TestCategories.Payment)]
public class PaymentResultServiceTests
{
    private static readonly DateTime Now = PaymentResultServiceTestBuilder.DefaultNow;
    private const string TxRef = "TX-1";

    private sealed record Order(Booking Booking, Invoice Invoice, Payment Payment, Guid PromotionId);

    /// <summary>Đơn 200.000 đ, cọc 60.000 đ đang chờ thanh toán, hạn sau 8 phút; giờ chơi 18:00 giờ VN ngày 21/11.</summary>
    private static Order CreateOrder(
        PaymentResultServiceTestBuilder builder, BookingStatus status = BookingStatus.PENDING,
        PaymentTxStatus paymentStatus = PaymentTxStatus.PENDING, DateTime? playStartUtc = null, DateTime? expiresAt = null)
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Chi nhánh 1", Address = "1 Lê Lợi", Phone = "0281234567" };
        var customer = TestDataFactory.CreateUser(email: "khach@example.com", fullName: "Nguyễn Văn A");
        var court = TestDataFactory.CreateCourt(branch.Id, "Sân Một");
        var playVn = DateTimeHelper.ToVietnamTime(playStartUtc ?? new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc));
        var booking = TestDataFactory.CreateBooking(customer.Id, branch.Id, status, bookingDate: DateOnly.FromDateTime(playVn));
        booking.BookingCode = "BK-001";
        booking.Customer = customer;
        booking.Branch = branch;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Court = court, Date = booking.BookingDate,
            StartTime = TimeOnly.FromDateTime(playVn), EndTime = TimeOnly.FromDateTime(playVn).AddHours(1), IsActive = true
        });
        var invoice = TestDataFactory.CreateInvoice(booking.Id, 200_000m, paymentTiming: PaymentTiming.PREPAID,
            expiresAt: expiresAt ?? Now.AddMinutes(8));
        invoice.DepositRate = 0.3m;
        invoice.DepositAmount = 60_000m;
        invoice.DepositSource = DepositSource.TADSS;
        invoice.Booking = booking;
        booking.Invoice = invoice;
        var payment = TestDataFactory.CreatePayment(invoice.Id, 60_000m, paymentStatus, PaymentTxMethod.VNPAY);
        payment.TransactionRef = TxRef;
        payment.Invoice = invoice;
        invoice.Payments.Add(payment);
        var promotionId = Guid.NewGuid();
        booking.BookingPromotion = new BookingPromotion { BookingId = booking.Id, PromotionId = promotionId };

        builder.Payments.Setup(x => x.GetByTransactionRefAsync(TxRef)).ReturnsAsync(payment);
        return new Order(booking, invoice, payment, promotionId);
    }

    private static GatewayPaymentResult Result(
        bool success = true, decimal? amount = 60_000m, bool signatureValid = true, IpnProvider provider = IpnProvider.VNPAY) =>
        new(provider, TxRef, signatureValid, success, amount, success ? "00" : "51", "raw");

    private static void VerifyNothingWritten(PaymentResultServiceTestBuilder builder)
    {
        builder.Payments.Verify(x => x.TryMarkSuccessAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
        builder.Payments.Verify(x => x.TryFailPendingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);
        builder.Bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        builder.Bookings.Verify(x => x.ShortenPaymentWindowAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        builder.Refunds.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
    }

    // ───────────────────────── Thứ tự khóa ─────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LocksTheBookingBeforeThePayment(bool success)
    {
        // Lệnh hủy giữ booking rồi chờ Payment: khóa Payment trước ở đây sẽ thành deadlock
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        var calls = new List<string>();
        builder.Bookings.Setup(x => x.LockAsync(order.Booking.Id)).Callback(() => calls.Add("booking")).Returns(Task.CompletedTask);
        builder.Payments.Setup(x => x.TryMarkSuccessAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .Callback(() => calls.Add("payment")).ReturnsAsync(true);
        builder.Payments.Setup(x => x.TryFailPendingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .Callback(() => calls.Add("payment")).ReturnsAsync(true);

        await builder.Build().ApplyAsync(Result(success: success));

        Assert.Equal(["booking", "payment"], calls.Take(2));
    }

    // ───────────────────────── Chặn trước khi ghi ─────────────────────────

    [Fact]
    public async Task InvalidSignature_IsLoggedButNothingWritten()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);

        await builder.Build().ApplyAsync(Result(signatureValid: false));

        builder.Payments.Verify(x => x.CreateIpnLogAsync(It.Is<PaymentIpnLog>(l =>
            l.PaymentId == order.Payment.Id && l.Provider == IpnProvider.VNPAY && l.ProviderTransactionId == TxRef
            && !l.IsValid && l.RawPayload == "raw" && l.ProcessedAt == Now)), Times.Once);
        VerifyNothingWritten(builder);
    }

    [Fact]
    public async Task UnknownTransaction_IsLoggedButNothingWritten()
    {
        var builder = new PaymentResultServiceTestBuilder();

        await builder.Build().ApplyAsync(Result());

        builder.Payments.Verify(x => x.CreateIpnLogAsync(It.Is<PaymentIpnLog>(l => l.PaymentId == null && l.IsValid)), Times.Once);
        VerifyNothingWritten(builder);
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(null)]
    public async Task AmountDifferentFromThePayment_NothingWritten(int? amount)
    {
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder);

        await builder.Build().ApplyAsync(Result(amount: amount));

        VerifyNothingWritten(builder);
        Assert.Contains(builder.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("lệch"));
    }

    // ───────────────────────── Thành công ─────────────────────────

    [Fact]
    public async Task Success_PendingOrder_ConfirmsAndVoidsOtherOpenLinks()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);

        await builder.Build().ApplyAsync(Result());

        builder.Payments.Verify(x => x.TryMarkSuccessAsync(order.Payment.Id, Now), Times.Once);
        builder.Bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            order.Booking.Id, BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), Now), Times.Once);
        builder.Invoices.Verify(x => x.SetPaymentStatusAsync(order.Invoice.Id, InvoicePaymentStatus.PARTIALLY_PAID, Now), Times.Once);
        builder.Payments.Verify(x => x.FailPendingByInvoiceAsync(order.Invoice.Id, order.Payment.Id, Now), Times.Once);
        builder.SlotLocks.Verify(x => x.DeleteByBookingIdAsync(order.Booking.Id), Times.Once);
        builder.Promotions.Verify(x => x.IncrementUsageCountAsync(order.PromotionId), Times.Once);
        builder.Refunds.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastPaymentEventAsync(SignalREvents.PaymentSuccess,
            It.Is<PaymentNotificationDto>(n => n.Status == "SUCCESS" && n.Amount == 60_000m), order.Booking, false), Times.Once);
    }

    [Fact]
    public async Task Success_ConfirmationEmailShowsDepositAndRemainder()
    {
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder);

        await builder.Build().ApplyAsync(Result());

        var email = Assert.Single(builder.Email.To("khach@example.com"));
        Assert.Contains("Đã cọc", email.Body);
        Assert.Contains(60_000m.ToString("N0"), email.Body);
        Assert.Contains("Còn lại thu tại quầy", email.Body);
        Assert.Contains(140_000m.ToString("N0"), email.Body);
    }

    [Fact]
    public async Task Success_FullPayment_EmailHasNoDepositLines()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        order.Invoice.DepositAmount = 200_000m;
        order.Invoice.DepositRate = 1m;
        order.Payment.Amount = 200_000m;

        await builder.Build().ApplyAsync(Result(amount: 200_000m));

        var email = Assert.Single(builder.Email.To("khach@example.com"));
        Assert.DoesNotContain("Đã cọc", email.Body);
        Assert.DoesNotContain("{{", email.Body);
    }

    [Fact]
    public async Task Success_FromAnotherGateway_RunsTheSameRules()
    {
        // Nghiệp vụ không đọc gì riêng của VNPay: kết quả của cổng khác cho cùng kết quả
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        order.Payment.Method = PaymentTxMethod.MOMO;

        await builder.Build().ApplyAsync(Result(provider: IpnProvider.MOMO));

        builder.Payments.Verify(x => x.CreateIpnLogAsync(It.Is<PaymentIpnLog>(l => l.Provider == IpnProvider.MOMO)), Times.Once);
        builder.Bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            order.Booking.Id, BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), Now), Times.Once);
    }

    [Fact]
    public async Task Success_SentTwice_SecondIsIgnored()
    {
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder);
        builder.Payments.Setup(x => x.TryMarkSuccessAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(false);

        await builder.Build().ApplyAsync(Result());

        builder.Bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        builder.Promotions.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Empty(builder.Email.Sent);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task Success_PlayWithin24Hours_CancelLinkExpiresAtPlayStart()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var playStart = Now.AddHours(3);
        CreateOrder(builder, playStartUtc: playStart);
        DateTime? expiry = null;
        string? hash = null;
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Callback((Guid _, BookingStatus _, string h, DateTime e, DateTime _) => { hash = h; expiry = e; })
            .ReturnsAsync(1);

        await builder.Build().ApplyAsync(Result());

        Assert.Equal(playStart, expiry);
        Assert.Equal(DateTimeKind.Utc, expiry!.Value.Kind);
        Assert.Matches("^[0-9a-f]{64}$", hash!);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public async Task Success_PlayDaysAway_CancelLinkExpiresTwentyFourHoursAfterPayment()
    {
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder, playStartUtc: Now.AddDays(3));
        DateTime? expiry = null;
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Callback((Guid _, BookingStatus _, string _, DateTime e, DateTime _) => expiry = e)
            .ReturnsAsync(1);

        await builder.Build().ApplyAsync(Result());

        Assert.Equal(Now.AddHours(24), expiry);
    }

    // ───────────────────────── Tiền về sau khi đơn đã hủy ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-22")]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task LatePayment_CancelledOrder_RefundsAllOfItAtTheBranch(BookingStatus cancelled)
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(order.Booking.Id)).ReturnsAsync(cancelled);

        await builder.Build().ApplyAsync(Result());

        builder.Refunds.Verify(x => x.CreateAsync(It.Is<Refund>(r =>
            r.PaymentId == order.Payment.Id && r.Amount == 60_000m && r.RefundPercent == 100 && r.Status == RefundStatus.PENDING
            && r.CreatedAt == Now)), Times.Once);
        builder.Bookings.Verify(x => x.MarkPendingRefundAsync(order.Booking.Id, cancelled, Now), Times.Once);
        builder.Promotions.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        builder.SlotLocks.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Contains(builder.Email.To("khach@example.com"), m => m.Body.Contains(60_000m.ToString("N0")));
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(SignalREvents.BookingCancelled,
            It.Is<BookingNotificationDto>(n => n.Status == nameof(BookingStatus.CANCELLED_PENDING_REFUND)), order.Booking, false), Times.Once);
        Assert.Contains(builder.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("sau khi booking"));
    }

    [Fact]
    public async Task LatePayment_OrderAlreadyWaitingForRefund_AddsAnotherRefundOnly()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(order.Booking.Id)).ReturnsAsync(BookingStatus.CANCELLED_PENDING_REFUND);

        await builder.Build().ApplyAsync(Result());

        builder.Refunds.Verify(x => x.CreateAsync(It.Is<Refund>(r => r.PaymentId == order.Payment.Id && r.Amount == 60_000m)), Times.Once);
        builder.Bookings.Verify(x => x.MarkPendingRefundAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task LatePayment_StatusChangesDuringTheWrite_ThrowsSoTheGatewayRetries()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(order.Booking.Id)).ReturnsAsync(BookingStatus.CANCELLED);
        builder.Bookings.Setup(x => x.MarkPendingRefundAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.Build().ApplyAsync(Result()));

        Assert.Empty(builder.Email.Sent);
    }

    // ───────────────────────── Trả hai lần ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE, LogLevel.Warning)]
    [InlineData(BookingStatus.CONFIRMED, LogLevel.Warning)]
    [InlineData(BookingStatus.IN_PROGRESS, LogLevel.Warning)]
    [InlineData(BookingStatus.COMPLETED, LogLevel.Error)]
    [InlineData(BookingStatus.NO_SHOW, LogLevel.Error)]
    public async Task ExtraPayment_ActiveOrFinishedOrder_IsRecordedWithoutChangingTheBooking(BookingStatus current, LogLevel level)
    {
        // Link cũ vẫn trả được (VNPAY.NET không gửi hạn phiên): khoản thừa giữ SUCCESS, trừ vào phần thu tại quầy khi check-out
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder, paymentStatus: PaymentTxStatus.FAILED);
        builder.Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(0);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(order.Booking.Id)).ReturnsAsync(current);

        await builder.Build().ApplyAsync(Result());

        builder.Payments.Verify(x => x.TryMarkSuccessAsync(order.Payment.Id, Now), Times.Once);
        builder.Refunds.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
        builder.Bookings.Verify(x => x.MarkPendingRefundAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()), Times.Never);
        builder.Invoices.Verify(x => x.SetPaymentStatusAsync(It.IsAny<Guid>(), It.IsAny<InvoicePaymentStatus>(), It.IsAny<DateTime>()), Times.Never);
        Assert.Contains(builder.Logger.Entries, e => e.Level == level && e.Message.Contains("nhận thêm"));
        Assert.Empty(builder.Email.Sent);
    }

    // ───────────────────────── Thất bại ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task Failure_PendingOrder_KeepsTheOrderAndShortensTheHoldToGrace()
    {
        // Hạn còn 8 phút: rút về 5 phút; đơn không bị hủy, sân không bị nhả
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder);

        await builder.Build().ApplyAsync(Result(success: false));

        builder.Payments.Verify(x => x.TryFailPendingAsync(order.Payment.Id, Now), Times.Once);
        builder.Bookings.Verify(x => x.ShortenPaymentWindowAsync(order.Booking.Id, Now.AddMinutes(5), Now), Times.Once);
        builder.SlotLocks.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Equal(BookingStatus.PENDING, order.Booking.Status);
        builder.Broadcast.Verify(x => x.BroadcastPaymentEventAsync(SignalREvents.PaymentFailed,
            It.Is<PaymentNotificationDto>(n => n.Status == "FAILED" && n.Message.Contains("09:05")), order.Booking, true), Times.Once);
    }

    [Fact]
    public async Task Failure_NearTheDeadline_NeverExtends()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder, expiresAt: Now.AddMinutes(3));

        await builder.Build().ApplyAsync(Result(success: false));

        builder.Bookings.Verify(x => x.ShortenPaymentWindowAsync(order.Booking.Id, Now.AddMinutes(3), Now), Times.Once);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task Failure_OfAnOldLink_ChangesNothing()
    {
        // Khách đã bấm thanh toán lại: kết quả thất bại của link cũ không được rút hạn đơn đang trả bằng link mới
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder, paymentStatus: PaymentTxStatus.FAILED);
        builder.Payments.Setup(x => x.TryFailPendingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(false);

        await builder.Build().ApplyAsync(Result(success: false));

        builder.Bookings.Verify(x => x.ShortenPaymentWindowAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastPaymentEventAsync(
            It.IsAny<string>(), It.IsAny<PaymentNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Failure_OrderNoLongerPending_ReportsWithoutTimeGrid()
    {
        var builder = new PaymentResultServiceTestBuilder();
        var order = CreateOrder(builder, BookingStatus.CANCELLED);
        builder.Bookings.Setup(x => x.ShortenPaymentWindowAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        await builder.Build().ApplyAsync(Result(success: false));

        builder.Broadcast.Verify(x => x.BroadcastPaymentEventAsync(SignalREvents.PaymentFailed,
            It.Is<PaymentNotificationDto>(n => !n.Message.Contains("giữ đến")), order.Booking, false), Times.Once);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotUndoTheRecordedPayment()
    {
        var builder = new PaymentResultServiceTestBuilder();
        CreateOrder(builder);
        builder.Broadcast.Setup(x => x.BroadcastPaymentEventAsync(
                It.IsAny<string>(), It.IsAny<PaymentNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("SignalR hỏng"));
        builder.Email.FailOnSend = true;

        await builder.Build().ApplyAsync(Result());

        Assert.Contains(builder.NotificationLogger.Entries, e => e.Level == LogLevel.Error);
    }
}
