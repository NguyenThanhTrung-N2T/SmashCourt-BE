using SmashCourt_BE.Data;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Jobs
{
    public class BookingJob : IBookingJob
    {
        private readonly SmashCourtContext _db;
        private readonly ILogger<BookingJob> _logger;

        public BookingJob(SmashCourtContext db, ILogger<BookingJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        // Hủy booking PENDING hết hạn
        public async Task CancelExpiredPendingBookingsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Where(b =>
                        b.Status == BookingStatus.PENDING &&
                        b.ExpiresAt < now)
                    .ToListAsync();

                foreach (var booking in expiredBookings)
                {
                    booking.Status = BookingStatus.CANCELLED;
                    booking.CancelledAt = now;
                    booking.CancelSource = CancelSourceEnum.SYSTEM;
                    booking.UpdatedAt = now;

                    foreach (var bc in booking.BookingCourts)
                    {
                        bc.IsActive = false;
                        if (bc.Court != null)
                        {
                            bc.Court.Status = CourtStatus.AVAILABLE;
                            bc.Court.UpdatedAt = now;
                        }
                    }
                }

                // Xóa slot_locks hết hạn liên quan
                await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)
                    .ExecuteDeleteAsync();

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Cancelled {Count} expired PENDING bookings", expiredBookings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling expired pending bookings");
            }
        }

        // Xử lý booking IN_PROGRESS + PAID_ONLINE + CONFIRMED hết giờ
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                var activeBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Where(b =>
                        b.Status == BookingStatus.IN_PROGRESS ||
                        b.Status == BookingStatus.PAID_ONLINE ||
                        b.Status == BookingStatus.CONFIRMED)
                    .ToListAsync();

                foreach (var booking in activeBookings)
                {
                    var bookingCourt = booking.BookingCourts.FirstOrDefault();
                    if (bookingCourt == null) continue;

                    var endDateTime = booking.BookingDate.ToDateTime(bookingCourt.EndTime);
                    if (endDateTime > now) continue;

                    var invoice = booking.Invoice;

                    switch (booking.Status)
                    {
                        case BookingStatus.IN_PROGRESS:
                            // Online + không có service fee phát sinh → tự động COMPLETED (đã thanh toán qua VNPay)
                            // Walk-in hoặc online có service fee → PENDING_PAYMENT để staff checkout tại quầy
                            if (booking.Source == BookingSource.ONLINE &&
                                invoice != null &&
                                invoice.ServiceFee == 0)
                            {
                                booking.Status = BookingStatus.COMPLETED;
                                invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                            }
                            else
                            {
                                // Walk-in (chưa thanh toán gì) hoặc online có thêm service fee → chờ checkout
                                booking.Status = BookingStatus.PENDING_PAYMENT;
                            }
                            break;

                        case BookingStatus.PAID_ONLINE:
                            // Hết giờ, khách không đến → COMPLETED
                            booking.Status = BookingStatus.COMPLETED;
                            if (invoice != null)
                                invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                            break;

                        case BookingStatus.CONFIRMED:
                            // Hết giờ, walk-in không check-in → CANCELLED
                            booking.Status = BookingStatus.CANCELLED;
                            booking.CancelledAt = now;
                            booking.CancelSource = CancelSourceEnum.SYSTEM;
                            foreach (var bc in booking.BookingCourts)
                                bc.IsActive = false;
                            break;
                    }

                    booking.UpdatedAt = now;
                    if (invoice != null) invoice.UpdatedAt = now;

                    // Cập nhật court → AVAILABLE
                    if (bookingCourt.Court != null &&
                        booking.Status != BookingStatus.PENDING_PAYMENT)
                    {
                        bookingCourt.Court.Status = CourtStatus.AVAILABLE;
                        bookingCourt.Court.UpdatedAt = now;
                    }
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation("Processed {Count} expired active bookings",
                    activeBookings.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing expired active bookings");
            }
        }

        // Xóa slot_locks hết hạn
        public async Task CleanupExpiredSlotLocksAsync()
        {
            try
            {
                var deleted = await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= DateTime.UtcNow)
                    .ExecuteDeleteAsync();

                if (deleted > 0)
                    _logger.LogInformation("Cleaned up {Count} expired slot locks", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up slot locks");
            }
        }
    }
}
