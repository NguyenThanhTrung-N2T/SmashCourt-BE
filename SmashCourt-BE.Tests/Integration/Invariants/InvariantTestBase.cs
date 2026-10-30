using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// Nền cho test của bộ kiểm SC6 (BE-IT-20): dựng đơn hợp lệ theo từng trạng thái rồi làm hỏng có chủ đích một điểm. "Now" của lượt kiểm là
/// <see cref="BookingDepositTestBase.Now"/>; slot lock của <see cref="BookingDepositTestBase.SeedOrderAsync"/> còn hạn tới CreatedAt + 10'.
/// </summary>
public abstract class InvariantTestBase(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    protected Task<IReadOnlyList<InvariantViolation>> CheckAsync(params Guid[] bookingIds) =>
        BookingInvariantChecker.CheckAsync(DbContext, BookingScope.Of(bookingIds), Now);

    protected static void AssertOnly(IReadOnlyList<InvariantViolation> violations, string rule, Guid bookingId, int count = 1)
    {
        Assert.True(violations.Count == count && violations.All(v => v.Rule == rule && v.BookingId == bookingId),
            $"Kỳ vọng {count} vi phạm {rule} của {bookingId}, thực tế:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    protected static void AssertNone(IReadOnlyList<InvariantViolation> violations) =>
        Assert.True(violations.Count == 0, $"Kỳ vọng 0 vi phạm, thực tế:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");

    /// <summary>Đơn đã thanh toán qua luồng cọc: hóa đơn có mức cọc, các Payment cho trước (SUCCESS có paid_at).</summary>
    protected async Task<PendingOrder> SeedDepositOrderAsync(
        BookingStatus status, decimal depositAmount, params (PaymentTxStatus Status, decimal Amount)[] payments)
    {
        var order = await SeedOrderAsync();
        order.Booking.Status = status;
        order.Invoice.DepositRate = depositAmount == 0 ? 0m : 1m;
        order.Invoice.DepositAmount = depositAmount;
        order.Invoice.DepositSource = depositAmount == 0 ? DepositSource.MANAGER : DepositSource.FULL_PAYMENT_FALLBACK;
        foreach (var (paymentStatus, amount) in payments)
            DbContext.Payments.Add(TestDataSeeder.NewPayment(order.Invoice.Id, paymentStatus, amount));
        await DbContext.SaveChangesAsync();
        return order;
    }

    /// <summary>Đơn đã hủy đúng thiết kế: dòng sân tắt, slot lock hết hạn tại Now, có lý do hủy.</summary>
    protected async Task<PendingOrder> SeedCancelledOrderAsync(BookingStatus status = BookingStatus.CANCELLED)
    {
        var order = await SeedOrderAsync(status);
        order.Booking.CancelReason = BookingCancelReason.PAYMENT_EXPIRED;
        foreach (var line in await DbContext.BookingCourts.Where(c => c.BookingId == order.Booking.Id).ToListAsync())
            line.IsActive = false;
        foreach (var slotLock in order.Locks)
            slotLock.ExpiresAt = Now;
        await DbContext.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Đơn một dòng sân, không slot lock (slot lock chồng giờ bị excl_slot_locks_no_overlap chặn kể cả khi hết hạn), dùng cho ca chồng giờ.
    /// Đơn PENDING có hóa đơn kèm hold_deadline; đơn hủy có lý do hủy.
    /// </summary>
    protected async Task<Booking> SeedLineAsync(
        BranchSeedData seed, Court court, BookingStatus status, string start, string end, bool isActive = true, DateOnly? date = null)
    {
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status, CreatedAt);
        if (status is BookingStatus.CANCELLED or BookingStatus.CANCELLED_PENDING_REFUND or BookingStatus.CANCELLED_REFUNDED)
            booking.CancelReason = BookingCancelReason.STAFF_CANCELLED;
        DbContext.Bookings.Add(booking);
        var invoice = TestDataSeeder.NewInvoice(booking);
        invoice.HoldDeadline = CreatedAt.AddMinutes(15);
        DbContext.Invoices.Add(invoice);
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
            booking.Id, court.Id, date ?? PlayDate, TimeOnly.Parse(start), TimeOnly.Parse(end), isActive));
        await DbContext.SaveChangesAsync();
        return booking;
    }
}
