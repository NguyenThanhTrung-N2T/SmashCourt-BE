using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>Dựng sẵn đơn online ở trạng thái ngay sau transaction 1 (booking, sân, hóa đơn có hạn, slot lock) để test transaction 2.</summary>
public abstract class BookingDepositTestBase(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    protected static readonly DateTime CreatedAt = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    protected static readonly DateTime Now = CreatedAt.AddSeconds(2);
    protected static readonly DateOnly PlayDate = new(2026, 11, 21);
    protected static readonly TimeOnly Start = new(18, 0);
    protected static readonly TimeOnly End = new(19, 0);

    protected BookingRepository Repository => new(DbContext);

    protected sealed record PendingOrder(Booking Booking, Invoice Invoice, BranchSeedData Seed, List<Court> Courts, List<SlotLock> Locks);

    /// <summary>
    /// Đơn như transaction 1 để lại: hóa đơn UNPAID/PREPAID, hết hạn sau 10', hold_deadline sau 15', mỗi sân một dòng booking_courts và một slot lock.
    /// </summary>
    protected async Task<PendingOrder> SeedOrderAsync(
        BookingStatus status = BookingStatus.PENDING, int courtCount = 1, BranchSeedData? seed = null, decimal finalTotal = 200_000m,
        DateOnly? playDate = null)
    {
        var date = playDate ?? PlayDate;
        seed ??= await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status, CreatedAt);
        if (status == BookingStatus.PENDING_APPROVAL)
        {
            // chk_bookings_pending_approval
            booking.TadssDecisionId = Guid.NewGuid();
            booking.ApprovalExpiresAt = CreatedAt.AddMinutes(30);
        }
        DbContext.Bookings.Add(booking);

        var invoice = TestDataSeeder.NewInvoice(booking, finalTotal);
        invoice.ExpiresAt = CreatedAt.AddMinutes(10);
        invoice.HoldDeadline = CreatedAt.AddMinutes(15);
        DbContext.Invoices.Add(invoice);
        await DbContext.SaveChangesAsync();

        var courts = new List<Court> { seed.Court };
        for (var i = 1; i < courtCount; i++)
            courts.Add(await Seeder.SeedCourtAsync(seed));

        var locks = new List<SlotLock>();
        foreach (var court in courts)
        {
            DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, court.Id, date, Start, End));
            var slotLock = TestDataSeeder.NewSlotLock(booking.Id, court.Id, date, Start, End, invoice.ExpiresAt!.Value);
            locks.Add(slotLock);
            DbContext.SlotLocks.Add(slotLock);
        }
        await DbContext.SaveChangesAsync();
        return new PendingOrder(booking, invoice, seed, courts, locks);
    }

    /// <summary>Đọc lại từ DB, bỏ qua change tracker (các hàm ExecuteUpdate không cập nhật entity đang được track).</summary>
    protected Task<Booking> ReloadBookingAsync(Guid bookingId) =>
        DbContext.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);

    protected Task<Invoice> ReloadInvoiceAsync(Guid bookingId) =>
        DbContext.Invoices.AsNoTracking().SingleAsync(i => i.BookingId == bookingId);

    protected Task<List<SlotLock>> ReloadLocksAsync(Guid bookingId) =>
        DbContext.SlotLocks.AsNoTracking().Where(l => l.BookingId == bookingId).ToListAsync();

    protected Task<List<BookingCourt>> ReloadCourtsAsync(Guid bookingId) =>
        DbContext.BookingCourts.AsNoTracking().Where(c => c.BookingId == bookingId).ToListAsync();
}
