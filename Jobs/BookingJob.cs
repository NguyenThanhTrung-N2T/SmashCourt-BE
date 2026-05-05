using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
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

        // Job-01: Hủy booking PENDING hết hạn (mỗi 1 phút)
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

                // lấy tất cả courtIds đang bị chiếm bởi booking active khác
                // → 1 query thay vì N AnyAsync trong vòng foreach
                var cancelledCourtIds = expiredBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                // Loại trừ chính các booking đang xử lý
                var cancelledBookingIds = expiredBookings.Select(b => b.Id).ToHashSet();

                var busyCourtIds = (await _db.BookingCourts
                    .Where(other =>
                        cancelledCourtIds.Contains(other.CourtId) &&
                        other.IsActive &&
                        !cancelledBookingIds.Contains(other.BookingId) && // ← loại trừ booking đang xử lý
                        ActiveStatuses.Contains(other.Booking.Status))
                    .Select(other => other.CourtId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

                foreach (var booking in expiredBookings)
                {
                    booking.Status = BookingStatus.CANCELLED;
                    booking.ExpiresAt = null;         
                    booking.CancelledAt = now;
                    booking.CancelSource = CancelSourceEnum.SYSTEM;
                    booking.UpdatedAt = now;

                    foreach (var bc in booking.BookingCourts)
                    {
                        bc.IsActive = false;

                        // chỉ set AVAILABLE nếu court không bị booking khác chiếm
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

        // Job-02: Xử lý booking hết giờ (mỗi 1 phút)
        public async Task ProcessExpiredActiveBookingsAsync()
        {
            try
            {
                var now = DateTime.UtcNow;

                // Xử lý booking hết giờ (EndTime)
                var activeBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Where(b =>
                        b.Status == BookingStatus.IN_PROGRESS ||
                        b.Status == BookingStatus.PAID_ONLINE ||
                        b.Status == BookingStatus.CONFIRMED)
                    .ToListAsync();

                if (activeBookings.Count == 0)
                {
                    await _db.SaveChangesAsync();
                    return;
                }

                // guard check court đang bị booking active khác dùng
                var affectedCourtIds = activeBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                // Loại trừ chính các booking đang xử lý
                var processingBookingIds = activeBookings.Select(b => b.Id).ToHashSet();

                var busyCourtIds = (await _db.BookingCourts
                    .Where(other =>
                        affectedCourtIds.Contains(other.CourtId) &&
                        other.IsActive &&
                        !processingBookingIds.Contains(other.BookingId) && // ← loại trừ booking đang xử lý
                        ActiveStatuses.Contains(other.Booking.Status))
                    .Select(other => other.CourtId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

                int processed = 0;

                foreach (var booking in activeBookings)
                {
                    if (!booking.BookingCourts.Any()) continue;

                    // Lấy max EndTime, convert sang UTC
                    var maxEndTime = booking.BookingCourts.Max(bc => bc.EndTime);
                    var vnEndDateTime = booking.BookingDate.ToDateTime(maxEndTime);
                    var utcEndDateTime = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(vnEndDateTime, DateTimeKind.Unspecified),
                        DateTimeHelper.VNTimezone);
                    
                    if (utcEndDateTime > now) continue;

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
                                
                                // Deactivate booking courts khi COMPLETED
                                foreach (var bc in booking.BookingCourts)
                                    bc.IsActive = false;
                            }
                            else
                            {
                                // Walk-in (chưa thu tiền) hoặc online có service fee
                                // → PENDING_PAYMENT để staff checkout tại quầy
                                booking.Status = BookingStatus.PENDING_PAYMENT;
                                
                                // Release slot ngay để đảm bảo nhất quán: Court = AVAILABLE + IsActive = false
                                foreach (var bc in booking.BookingCourts)
                                {
                                    bc.IsActive = false;
                                    
                                    // Chỉ set AVAILABLE nếu court không bị booking khác chiếm
                                    if (bc.Court != null && !busyCourtIds.Contains(bc.CourtId))
                                    {
                                        bc.Court.Status = CourtStatus.AVAILABLE;
                                        bc.Court.UpdatedAt = now;
                                    }
                                }
                            }
                            break;

                        case BookingStatus.PAID_ONLINE:
                            // Khách đã thanh toán nhưng chưa check-in, bỏ qua để NO_SHOW job xử lý
                            if (booking.CheckedInAt == null)
                            {
                                continue;
                            }
                            
                            // Đã check-in nhưng hết giờ → COMPLETED
                            booking.Status = BookingStatus.COMPLETED;
                            if (invoice != null)
                            {
                                invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                                invoice.UpdatedAt = now;
                            }
                            // Deactivate booking courts
                            foreach (var bc in booking.BookingCourts)
                                bc.IsActive = false;
                            break;

                        case BookingStatus.CONFIRMED:
                            // Walk-in chưa check-in, bỏ qua để NO_SHOW job xử lý
                            if (booking.CheckedInAt == null)
                            {
                                continue;
                            }
                            
                            // Đã check-in, xử lý như IN_PROGRESS
                            booking.Status = BookingStatus.PENDING_PAYMENT;
                            
                            // Release slot ngay
                            foreach (var bc in booking.BookingCourts)
                            {
                                bc.IsActive = false;
                                
                                if (bc.Court != null && !busyCourtIds.Contains(bc.CourtId))
                                {
                                    bc.Court.Status = CourtStatus.AVAILABLE;
                                    bc.Court.UpdatedAt = now;
                                }
                            }
                            break;
                    }

                    booking.UpdatedAt = now;

                    // Update court status → AVAILABLE khi booking kết thúc
                    // Áp dụng cho: COMPLETED
                    // KHÔNG áp dụng cho: PENDING_PAYMENT (đã release ở trên)
                    if (booking.Status == BookingStatus.COMPLETED)
                    {
                        foreach (var bc in booking.BookingCourts)
                        {
                            // Guard: chỉ set AVAILABLE nếu court không bị booking khác chiếm
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

        // Job-03: Xóa slot_locks hết hạn (mỗi 30 giây)
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

        // Job-04: Phát hiện NO_SHOW (mỗi 5 phút)
        // Đánh dấu booking là NO_SHOW khi khách không đến sau 15 phút kể từ StartTime
        public async Task DetectNoShowBookingsAsync()
        {
            try
            {
                _logger.LogInformation("Starting NO_SHOW detection job");

                var now = DateTime.UtcNow;
                
                // Lấy danh sách status đủ điều kiện cho NO_SHOW từ helper
                var noShowEligibleStatuses = BookingStatusTransition.GetNoShowEligibleStatuses();

                // Query bookings đủ điều kiện: CONFIRMED hoặc PAID_ONLINE chưa check-in
                var eligibleBookings = await _db.Bookings
                    .Include(b => b.BookingCourts)
                        .ThenInclude(bc => bc.Court)
                    .Include(b => b.Invoice)
                    .Where(b => noShowEligibleStatuses.Contains(b.Status) 
                                && b.CheckedInAt == null)
                    .ToListAsync();

                if (eligibleBookings.Count == 0)
                {
                    _logger.LogInformation("No bookings to process for NO_SHOW detection");
                    return;
                }

                var noShowBookings = new List<Booking>();

                foreach (var booking in eligibleBookings)
                {
                    if (!booking.BookingCourts.Any()) continue;

                    // Lấy StartTime sớm nhất của booking
                    var minStartTime = booking.BookingCourts.Min(bc => bc.StartTime);
                    
                    // Tạo Booking_DateTime với DateTimeKind.Utc
                    var bookingDateTime = DateTime.SpecifyKind(
                        booking.BookingDate.ToDateTime(minStartTime),
                        DateTimeKind.Utc);

                    // Kiểm tra đã quá 15 phút sau StartTime chưa
                    if (bookingDateTime.AddMinutes(15) < now)
                    {
                        noShowBookings.Add(booking);
                    }
                }

                if (noShowBookings.Count == 0)
                {
                    _logger.LogInformation("No NO_SHOW bookings detected");
                    return;
                }

                // Lấy danh sách courtIds bị ảnh hưởng
                var affectedCourtIds = noShowBookings
                    .SelectMany(b => b.BookingCourts)
                    .Select(bc => bc.CourtId)
                    .Distinct()
                    .ToHashSet();

                // Loại trừ chính các booking đang xử lý
                var processingBookingIds = noShowBookings.Select(b => b.Id).ToHashSet();

                // Kiểm tra court đang bị booking active khác dùng
                var busyCourtIds = (await _db.BookingCourts
                    .Where(other =>
                        affectedCourtIds.Contains(other.CourtId) &&
                        other.IsActive &&
                        !processingBookingIds.Contains(other.BookingId) &&
                        ActiveStatuses.Contains(other.Booking.Status))
                    .Select(other => other.CourtId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();

                int markedCount = 0;

                foreach (var booking in noShowBookings)
                {
                    try
                    {
                        // Đánh dấu NO_SHOW
                        booking.Status = BookingStatus.NO_SHOW;
                        booking.UpdatedAt = now;

                        // Deactivate booking courts
                        foreach (var bc in booking.BookingCourts)
                        {
                            bc.IsActive = false;

                            // Update court status → AVAILABLE nếu không bị booking khác chiếm
                            if (bc.Court != null && !busyCourtIds.Contains(bc.CourtId))
                            {
                                // Chỉ set AVAILABLE nếu court không ở trạng thái đặc biệt
                                if (bc.Court.Status != CourtStatus.SUSPENDED &&
                                    bc.Court.Status != CourtStatus.INACTIVE)
                                {
                                    bc.Court.Status = CourtStatus.AVAILABLE;
                                    bc.Court.UpdatedAt = now;
                                }
                            }
                        }

                        markedCount++;
                        
                        // Log chi tiết cho NO_SHOW
                        var minStartTime = booking.BookingCourts.Min(bc => bc.StartTime);
                        _logger.LogWarning(
                            "[NO_SHOW] Booking {BookingId} marked as NO_SHOW. " +
                            "Customer: {CustomerId}, Courts: [{CourtIds}], " +
                            "BookingDate: {BookingDate}, StartTime: {StartTime}, " +
                            "PaymentTiming: {PaymentTiming}, Amount: {Amount}",
                            booking.Id,
                            booking.CustomerId ?? Guid.Empty,
                            string.Join(", ", booking.BookingCourts.Select(bc => bc.CourtId)),
                            booking.BookingDate,
                            minStartTime,
                            booking.Invoice?.PaymentTiming.ToString() ?? "N/A",
                            booking.Invoice?.FinalTotal ?? 0
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "Error marking booking {BookingId} as NO_SHOW", booking.Id);
                        // Tiếp tục xử lý các booking khác
                    }
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Marked {Count} bookings as NO_SHOW", markedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NO_SHOW detection job");
            }
        }
    }
}
