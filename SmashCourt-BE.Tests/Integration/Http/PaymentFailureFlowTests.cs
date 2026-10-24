using System.Net;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Thanh toán thất bại và tiền về sau khi hủy, chạy đúng Program.cs trên schema thật: thẻ lỗi không hủy đơn mà giữ thêm tối đa 5 phút,
/// thanh toán lại dừng ở hạn giữ chỗ tuyệt đối, kết quả của link cũ không đổi đơn, tiền về đơn đã hủy được hoàn 100 % tại chi nhánh.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Payment")]
public sealed class PaymentFailureFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    // ───────────────────────── BE-IT-21 ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task CardDeclined_KeepsTheOrderAndShortensTheHoldToFiveMinutes()
    {
        var order = await BookPendingAsync();
        var before = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount, success: false));

        var after = DateTime.UtcNow;
        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Null(booking.CancelledAt);
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single(booking.Invoice!.Payments).Status);
        Assert.InRange(booking.Invoice.ExpiresAt!.Value, before.AddMinutes(5).AddSeconds(-1), after.AddMinutes(5).AddSeconds(1));
        Assert.All(booking.SlotLocks, l => Assert.Equal(booking.Invoice.ExpiresAt, l.ExpiresAt));
        Assert.All(booking.BookingCourts, bc => Assert.True(bc.IsActive));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task AfterADecline_RetryWithinGrace_ThenPaysSuccessfully()
    {
        var order = await BookPendingAsync();
        await IpnAsync(order.TransactionRef, order.Amount, success: false);

        var retry = await RetryAsync(order.Customer, order.BookingId);
        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        var newPayment = (await RequireBookingAsync(order.BookingId)).Invoice!.Payments.Single(p => p.Status == PaymentTxStatus.PENDING);

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(newPayment.TransactionRef!, order.Amount));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, booking.Invoice!.Payments.Single(p => p.Id == newPayment.Id).Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task ApprovedDepositDeclined_HoldShrinksToGrace_ThenPaysTheApprovedDepositNotTheTotal()
    {
        // Đơn vừa được duyệt không mất vì một lần thẻ lỗi; lần trả lại vẫn là tiền cọc đã duyệt (60.000 đ), không phải 200.000 đ
        var pending = await ApproveDepositAsync();
        var first = await StartPaymentAsync(pending.Customer, pending.BookingId);
        Assert.Equal(60_000m, first.Amount);
        var approvedDeadline = (await RequireBookingAsync(pending.BookingId)).Invoice!.ExpiresAt!.Value;
        var before = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(first.TransactionRef!, first.Amount, success: false));

        var after = DateTime.UtcNow;
        var declined = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING, declined.Status);
        var shrunk = declined.Invoice!.ExpiresAt!.Value;
        Assert.InRange(shrunk, before.AddMinutes(5).AddSeconds(-1), after.AddMinutes(5).AddSeconds(1));
        Assert.True(shrunk < approvedDeadline, $"Hạn sau thẻ lỗi {shrunk:O} phải ngắn hơn hạn trả cọc {approvedDeadline:O}");
        Assert.All(declined.SlotLocks, l => Assert.Equal(shrunk, l.ExpiresAt));

        var retry = await RetryAsync(pending.Customer, pending.BookingId);
        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        Assert.Equal(60_000m, VnPayAmount(retry.DataString("paymentUrl")!));
        var second = (await RequireBookingAsync(pending.BookingId)).Invoice!.Payments.Single(p => p.Status == PaymentTxStatus.PENDING);
        Assert.Equal(60_000m, second.Amount);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(second.TransactionRef!, second.Amount));

        var paid = await RequireBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, paid.Status);
        Assert.Equal(60_000m, paid.Invoice!.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
        Assert.Equal(60_000m, paid.Invoice.DepositAmount);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task DeclineOfAnOldLink_DoesNotTouchTheOrderPayingWithTheNewLink()
    {
        var order = await BookPendingAsync();
        await AssertApiStatusAsync(HttpStatusCode.OK, await RetryAsync(order.Customer, order.BookingId));
        var expiresBefore = (await RequireBookingAsync(order.BookingId)).Invoice!.ExpiresAt;

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount, success: false));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(expiresBefore, booking.Invoice!.ExpiresAt);
        Assert.Single(booking.Invoice.Payments, p => p.Status == PaymentTxStatus.PENDING);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task RetryAfterTheHoldDeadline_PaymentHoldExpired()
    {
        // Bấm thanh toán lại liên tục không kéo được quá hạn giữ chỗ tuyệt đối
        var order = await BookPendingAsync();
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HoldDeadline, (DateTime?)DateTime.UtcNow.AddSeconds(-1))));

        var retry = await RetryAsync(order.Customer, order.BookingId);

        Assert.Equal(HttpStatusCode.BadRequest, retry.Status);
        Assert.Equal(ErrorCodes.PaymentHoldExpired, retry.Code);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task DeclinedThenAbandoned_PaymentExpiryJobFreesTheSlot()
    {
        var order = await BookPendingAsync();
        await IpnAsync(order.TransactionRef, order.Amount, success: false);
        // Hết ân hạn: hạn của hóa đơn và slot lock (luôn bằng nhau) đã qua
        var expired = DateTime.UtcNow.AddSeconds(-1);
        await WithDbAsync(async db =>
        {
            await db.SlotLocks.Where(l => l.BookingId == order.BookingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expired));
            return await db.Invoices.Where(i => i.BookingId == order.BookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-30))
                .SetProperty(i => i.ExpiresAt, (DateTime?)expired));
        });

        await RunPaymentExpiryJobAsync();

        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.All(booking.BookingCourts, c => Assert.False(c.IsActive));
        Assert.Empty(booking.SlotLocks);
    }

    // ───────────────────────── BE-IT-22 ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-22")]
    public async Task MoneyArrivesAfterTheOrderExpired_RefundsAllOfItAtTheBranch()
    {
        var order = await BookPendingAsync();
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-30))
                .SetProperty(i => i.ExpiresAt, (DateTime?)DateTime.UtcNow.AddSeconds(-1))));
        await RunPaymentExpiryJobAsync();
        var cancelled = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, cancelled.Status);

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        Assert.Equal(cancelled.CancelReason, booking.CancelReason);
        Assert.Equal(cancelled.CancelledAt, booking.CancelledAt);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice!.Payments).Status);
        var refund = Assert.Single(await ReadRefundsAsync(order.BookingId));
        Assert.Equal(order.Amount, refund.Amount);
        Assert.Equal(100m, refund.RefundPercent);
        Assert.Equal(RefundStatus.PENDING, refund.Status);

        // IPN gửi lại: không thêm Refund
        await IpnAsync(order.TransactionRef, order.Amount);
        Assert.Single(await ReadRefundsAsync(order.BookingId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-22")]
    public async Task CustomerCancelledThenMoneyArrives_StaffConfirmsTheCashRefund()
    {
        var order = await BookPendingAsync();
        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(order.BookingId)).Status);

        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, booking.Status);
        Assert.Equal(BookingCancelReason.CUSTOMER_CANCELLED, booking.CancelReason);
        Assert.Equal(InvoicePaymentStatus.REFUNDED, booking.Invoice!.PaymentStatus);
        Assert.Equal(order.Amount, Assert.Single(booking.Invoice.Payments).RefundedAmount);
        Assert.Equal(RefundStatus.COMPLETED, Assert.Single(await ReadRefundsAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task SecondLinkPaidAfterTheFirst_IsRecordedWithoutChangingTheBooking()
    {
        // VNPAY.NET không gửi hạn phiên: link cũ vẫn trả được sau "Thanh toán lại"
        var order = await BookPendingAsync();
        await AssertApiStatusAsync(HttpStatusCode.OK, await RetryAsync(order.Customer, order.BookingId));
        var second = (await RequireBookingAsync(order.BookingId)).Invoice!.Payments.Single(p => p.Status == PaymentTxStatus.PENDING);

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(second.TransactionRef!, order.Amount));
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PAID_ONLINE, booking.Status);
        Assert.Equal(2, booking.Invoice!.Payments.Count(p => p.Status == PaymentTxStatus.SUCCESS));
        Assert.Empty(await ReadRefundsAsync(order.BookingId));
    }
}
