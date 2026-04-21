using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Jobs
{
    public class BookingJob : IBookingJob
    {
        private readonly SmashCourtContext _db;
        private readonly ILogger<BookingJob> _logger;

        // Các status được coi là "booking đang chiếm sân"
        private static readonly BookingStatus[] ActiveStatuses =
        [
            BookingStatus.CONFIRMED,
            BookingStatus.PAID_ONLINE,
            BookingStatus.IN_PROGRESS
        ];

        public BookingJob(SmashCourtContext db, ILogger<BookingJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ── Job-01: Hủy booking PENDING hết hạn (mỗi 1 phút) ─────────────────
        public async Task CancelExpiredPendingBookingsAsync()
        {
            try
            {
                // timestamptz đọc từ DB ra là Kind=Utc → so sánh với UTC
                var now = DateTime.UtcNow;
                var expiredBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Where(b =>
                        b.Status == BookingStatus.PENDING &&
                        b.ExpiresAt < now)
                    .ToListAsync();

                if (expiredBookings.Count == 0) return;

                // ✅ Batch load: lấy tất cả courtIds đang bị chiếm bởi booking active khác
                // → 1 query thay vì N AnyAsync trong vòng foreach
                var cancelledCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var busyCourtIds = (await _db.BookingCourts
                    .Where(other =>
                        cancelledCourtIds.Contains(other.CourtId) &&
                        other.IsActive &&
                        ActiveStatuses.Contains(other.Booking.Status))
                    .Select(other => other.CourtId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

                foreach (var booking in expiredBookings)
                {
                    booking.Status = BookingStatus.CANCELLED;
                    booking.CancelledAt = now;
                    booking.CancelSource = CancelSourceEnum.SYSTEM;
                    booking.UpdatedAt = now;

                    foreach (var bc in booking.BookingCourts)
                    {
                        bc.IsActive = false;

                        // ✅ Guard: chỉ set AVAILABLE nếu court không bị booking khác chiếm
                        if (bc.Court != null && !busyCourtIds.Contains(bc.CourtId))
                        {
                            bc.Court.Status = CourtStatus.AVAILABLE;
                            bc.Court.UpdatedAt = now;
                        }
                    }
                }

                await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= now)  // now = UTC
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

        // ── Job-03+04: Xử lý booking hết giờ (mỗi 1 phút) ──────────────────
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            try
            {
                // timestamptz đọc từ DB ra là Kind=Utc → so sánh với UTC
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

                if (activeBookings.Count == 0) return;

                // ✅ Batch load: guard check court đang bị booking active khác dùng
                var affectedCourtIds = activeBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                var busyCourtIds = (await _db.BookingCourts
                    .Where(other =>
                        affectedCourtIds.Contains(other.CourtId) &&
                        other.IsActive &&
                        ActiveStatuses.Contains(other.Booking.Status))
                    .Select(other => other.CourtId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

                int processed = 0;

                foreach (var booking in activeBookings)
                {
                    if (!booking.BookingCourts.Any()) continue;

                    // ✅ FIX: Use Max(EndTime) across all courts for multi-court bookings
                    var maxEndTime = booking.BookingCourts.Max(bc => bc.EndTime);
                    // Convert endTime sang UTC để so sánh với now (UTC)
                    var endDateTimeVn = booking.BookingDate.ToDateTime(maxEndTime);
                    var endDateTimeUtc = TimeZoneInfo.ConvertTimeToUtc(
                        endDateTimeVn, DateTimeHelper.VNTimezone);

                    if (endDateTimeUtc > now) continue;

                    var invoice = booking.Invoice;

                    switch (booking.Status)
                    {
                        case BookingStatus.IN_PROGRESS:
                            // Online đã thanh toán sân, không có service fee phát sinh
                            // → tự động COMPLETED (VNPay đã thu tiền rồi)
                            if (booking.Source == BookingSource.ONLINE &&
                                invoice != null &&
                                invoice.ServiceFee == 0)
                            {
                                booking.Status = BookingStatus.COMPLETED;
                                invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                                invoice.UpdatedAt = now;
                            }
                            else
                            {
                                // Walk-in (chưa thu tiền) hoặc online có service fee
                                // → PENDING_PAYMENT để staff checkout tại quầy
                                booking.Status = BookingStatus.PENDING_PAYMENT;
                                // Court giữ nguyên IN_USE cho đến khi staff checkout
                            }
                            break;

                        case BookingStatus.PAID_ONLINE:
                            // Đã thanh toán online, hết giờ (dù có hoặc không check-in)
                            // → COMPLETED, tiền đã thu
                            booking.Status = BookingStatus.COMPLETED;
                            if (invoice != null)
                            {
                                invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                                invoice.UpdatedAt = now;
                            }
                            break;

                        case BookingStatus.CONFIRMED:
                            // Walk-in no-show: hết giờ mà chưa check-in → CANCELLED
                            booking.Status = BookingStatus.CANCELLED;
                            booking.CancelledAt = now;
                            booking.CancelSource = CancelSourceEnum.SYSTEM;
                            foreach (var bc in booking.BookingCourts)
                                bc.IsActive = false;
                            break;
                    }

                    booking.UpdatedAt = now;

                    // ✅ FIX: Update ALL courts → AVAILABLE when booking ends
                    // Không set khi PENDING_PAYMENT — staff còn cần thao tác checkout
                    if (booking.Status == BookingStatus.COMPLETED ||
                        booking.Status == BookingStatus.CANCELLED)
                    {
                        foreach (var bc in booking.BookingCourts)
                        {
                            if (bc.Court != null && !busyCourtIds.Contains(bc.CourtId))
                            {
                                bc.Court.Status = CourtStatus.AVAILABLE;
                                bc.Court.UpdatedAt = now;
                            }
                        }
                    }

                    processed++;
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Processed {Count} expired active bookings", processed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing expired active bookings");
            }
        }

        // ── Job-02: Xóa slot_locks hết hạn (mỗi 30 giây) ────────────────────
        // Không cần update Court.Status ở đây:
        // SlotLock.ExpiresAt == Booking.ExpiresAt (cùng variable trong CreateOnlineAsync)
        // → Job-01 sẽ handle Court.Status khi booking PENDING expire cùng lúc
        public async Task CleanupExpiredSlotLocksAsync()
        {
            try
            {
                var deleted = await _db.SlotLocks
                    .Where(sl => sl.ExpiresAt <= DateTime.UtcNow)  // UTC
                    .ExecuteDeleteAsync();

                if (deleted > 0)
                    _logger.LogInformation(
                        "Cleaned up {Count} expired slot locks", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up slot locks");
            }
        }
    }
}
