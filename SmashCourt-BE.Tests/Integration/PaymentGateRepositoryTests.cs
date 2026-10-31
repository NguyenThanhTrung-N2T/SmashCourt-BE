using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Câu ghi có điều kiện của luồng thanh toán và hoàn tiền, trên schema thật: mỗi câu chỉ thắng khi dữ liệu còn đúng như lúc đọc.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PaymentGateRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private PaymentRepository Payments => new(DbContext);
    private RefundRepository Refunds => new(DbContext);

    private async Task<Payment> AddPaymentAsync(Guid invoiceId, PaymentTxStatus status, decimal amount = 60_000m)
    {
        var payment = TestDataSeeder.NewPayment(invoiceId, status, amount);
        DbContext.Payments.Add(payment);
        await DbContext.SaveChangesAsync();
        return payment;
    }

    private Task<Payment> ReloadPaymentAsync(Guid id) => DbContext.Payments.AsNoTracking().SingleAsync(p => p.Id == id);

    // ───────────────────────── Payment ─────────────────────────

    [Theory]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED)]
    public async Task TryMarkSuccess_FromPendingOrFailed_RecordsTheMoney(PaymentTxStatus from)
    {
        // Link cũ đã bị đánh FAILED khi thanh toán lại vẫn phải được ghi nhận khi tiền về
        var order = await SeedOrderAsync();
        var payment = await AddPaymentAsync(order.Invoice.Id, from);

        Assert.True(await Payments.TryMarkSuccessAsync(payment.Id, Now));

        var stored = await ReloadPaymentAsync(payment.Id);
        Assert.Equal(PaymentTxStatus.SUCCESS, stored.Status);
        Assert.Equal(Now, stored.PaidAt);
        Assert.Equal(Now, stored.UpdatedAt);
    }

    [Fact]
    public async Task TryMarkSuccess_Twice_SecondCallLoses()
    {
        var order = await SeedOrderAsync();
        var payment = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.PENDING);

        Assert.True(await Payments.TryMarkSuccessAsync(payment.Id, Now));
        Assert.False(await Payments.TryMarkSuccessAsync(payment.Id, Now.AddSeconds(5)));

        Assert.Equal(Now, (await ReloadPaymentAsync(payment.Id)).PaidAt);
    }

    [Fact]
    public async Task TryFailPending_OnlyFromPending()
    {
        var order = await SeedOrderAsync();
        var pending = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.PENDING);
        var oldLink = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.FAILED);
        var paid = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS);

        Assert.True(await Payments.TryFailPendingAsync(pending.Id, Now));
        Assert.False(await Payments.TryFailPendingAsync(oldLink.Id, Now));
        Assert.False(await Payments.TryFailPendingAsync(paid.Id, Now));

        Assert.Equal(PaymentTxStatus.FAILED, (await ReloadPaymentAsync(pending.Id)).Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, (await ReloadPaymentAsync(paid.Id)).Status);
    }

    [Fact]
    public async Task FailPendingByInvoice_SkipsTheExceptedPaymentAndOtherInvoices()
    {
        var order = await SeedOrderAsync();
        var other = await SeedOrderAsync();
        var winner = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS);
        var stale = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.PENDING);
        var excepted = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.PENDING);
        var otherPending = await AddPaymentAsync(other.Invoice.Id, PaymentTxStatus.PENDING);

        var rows = await Payments.FailPendingByInvoiceAsync(order.Invoice.Id, excepted.Id, Now);

        Assert.Equal(1, rows);
        Assert.Equal(PaymentTxStatus.FAILED, (await ReloadPaymentAsync(stale.Id)).Status);
        Assert.Equal(PaymentTxStatus.PENDING, (await ReloadPaymentAsync(excepted.Id)).Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, (await ReloadPaymentAsync(winner.Id)).Status);
        Assert.Equal(PaymentTxStatus.PENDING, (await ReloadPaymentAsync(otherPending.Id)).Status);
    }

    [Fact]
    public async Task GetSuccessfulByInvoice_ReturnsOnlyCollectedPayments()
    {
        var order = await SeedOrderAsync();
        var first = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS);
        await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.PENDING);
        await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.FAILED);
        var second = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS, 30_000m);

        var paid = await Payments.GetSuccessfulByInvoiceAsync(order.Invoice.Id);

        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(i => i), paid.Select(p => p.Id).OrderBy(i => i));
    }

    // ───────────────────────── Rút hạn khi thanh toán thất bại ─────────────────────────

    [Fact]
    public async Task ShortenPaymentWindow_PendingOrder_ShortensInvoiceAndLocks()
    {
        var order = await SeedOrderAsync(courtCount: 2);
        var shorter = CreatedAt.AddMinutes(7);

        Assert.True(await Repository.ShortenPaymentWindowAsync(order.Booking.Id, shorter, Now));
        Assert.Equal(shorter, (await ReloadInvoiceAsync(order.Booking.Id)).ExpiresAt);
        Assert.All(await ReloadLocksAsync(order.Booking.Id), l => Assert.Equal(shorter, l.ExpiresAt));
        Assert.Equal(CreatedAt.AddMinutes(15), (await ReloadInvoiceAsync(order.Booking.Id)).HoldDeadline);
    }

    [Fact]
    public async Task ShortenPaymentWindow_NeverExtends()
    {
        var order = await SeedOrderAsync();

        Assert.True(await Repository.ShortenPaymentWindowAsync(order.Booking.Id, CreatedAt.AddMinutes(14), Now));

        Assert.Equal(CreatedAt.AddMinutes(10), (await ReloadInvoiceAsync(order.Booking.Id)).ExpiresAt);
        Assert.All(await ReloadLocksAsync(order.Booking.Id), l => Assert.Equal(CreatedAt.AddMinutes(10), l.ExpiresAt));
    }

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CONFIRMED)]
    public async Task ShortenPaymentWindow_OrderNoLongerPending_Loses(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);

        Assert.False(await Repository.ShortenPaymentWindowAsync(order.Booking.Id, CreatedAt.AddMinutes(7), Now));

        Assert.Equal(CreatedAt.AddMinutes(10), (await ReloadInvoiceAsync(order.Booking.Id)).ExpiresAt);
    }

    [Fact]
    public async Task ShortenPaymentWindow_OutsideTransaction_Throws()
    {
        // Context riêng, không mở transaction nào: rút hạn hóa đơn mà không rút slot lock sẽ để lại dữ liệu lệch
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.ShortenPaymentWindowAsync(Guid.NewGuid(), CreatedAt.AddMinutes(7), Now));
    }

    [Fact]
    public async Task ShortenPaymentWindow_InsideTransactionScope_RunsNormally()
    {
        // Đường chạy thật của service dùng TransactionScope
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        Assert.False(await repository.ShortenPaymentWindowAsync(Guid.NewGuid(), CreatedAt.AddMinutes(7), Now));
    }

    // ───────────────────────── Tiền về sau khi hủy ─────────────────────────

    private async Task<Booking> SeedCancelledAsync(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);
        order.Booking.CancelledAt = CreatedAt.AddMinutes(11);
        order.Booking.CancelSource = CancelSourceEnum.SYSTEM;
        order.Booking.CancelReason = BookingCancelReason.PAYMENT_EXPIRED;
        await DbContext.SaveChangesAsync();
        return order.Booking;
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task MarkPendingRefund_KeepsTheCancelReason(BookingStatus from)
    {
        var booking = await SeedCancelledAsync(from);

        Assert.True(await Repository.MarkPendingRefundAsync(booking.Id, from, Now));

        var stored = await ReloadBookingAsync(booking.Id);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, stored.Status);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, stored.CancelReason);
        Assert.Equal(CancelSourceEnum.SYSTEM, stored.CancelSource);
        Assert.Equal(CreatedAt.AddMinutes(11), stored.CancelledAt);
    }

    [Fact]
    public async Task MarkPendingRefund_StatusChangedSinceRead_Loses()
    {
        var booking = await SeedCancelledAsync(BookingStatus.CANCELLED_PENDING_REFUND);

        Assert.False(await Repository.MarkPendingRefundAsync(booking.Id, BookingStatus.CANCELLED, Now));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    public async Task MarkPendingRefund_FromNonCancelledOrAlreadyPending_Throws(BookingStatus expected)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Repository.MarkPendingRefundAsync(Guid.NewGuid(), expected, Now));
    }

    // ───────────────────────── Refund ─────────────────────────

    [Fact]
    public async Task GetPendingRefunds_ReturnsEveryPendingRefundOfTheBooking()
    {
        var order = await SeedOrderAsync(BookingStatus.CANCELLED_PENDING_REFUND);
        var other = await SeedOrderAsync(BookingStatus.CANCELLED_PENDING_REFUND);
        var first = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS);
        var second = await AddPaymentAsync(order.Invoice.Id, PaymentTxStatus.SUCCESS);
        var otherPayment = await AddPaymentAsync(other.Invoice.Id, PaymentTxStatus.SUCCESS);
        var pendingA = TestDataSeeder.NewRefund(first.Id, 100, 60_000m);
        var pendingB = TestDataSeeder.NewRefund(second.Id, 100, 60_000m);
        var done = TestDataSeeder.NewRefund(first.Id, 50, 1_000m);
        var otherRefund = TestDataSeeder.NewRefund(otherPayment.Id, 100, 60_000m);
        pendingA.Status = pendingB.Status = otherRefund.Status = RefundStatus.PENDING;
        DbContext.Refunds.AddRange(pendingA, pendingB, done, otherRefund);
        await DbContext.SaveChangesAsync();

        var refunds = await Refunds.GetPendingByBookingIdAsync(order.Booking.Id);

        Assert.Equal(new[] { pendingA.Id, pendingB.Id }.OrderBy(i => i), refunds.Select(r => r.Id).OrderBy(i => i));
        Assert.All(refunds, r => Assert.NotNull(r.Payment));
    }
}
