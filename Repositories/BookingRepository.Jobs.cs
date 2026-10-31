using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Repositories
{
    /// <summary>
    /// Truy vấn và câu ghi có điều kiện của các job đổi trạng thái booking và job báo kết quả về TADSS (gói 6A-5, P6-25, P6-26).
    /// Ứng viên chỉ đọc không khóa; mỗi đơn được ghi trong transaction riêng của caller bằng câu có điều kiện chạy đầu tiên.
    /// </summary>
    public partial class BookingRepository
    {
        private static readonly BookingStatus[] CancelledStatuses =
            [BookingStatus.CANCELLED, BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED];

        // Đơn đã chơi (hoặc không đến) chỉ báo sau giờ kết thúc; PENDING_PAYMENT là khách đã chơi mà chưa check-out
        private static readonly BookingStatus[] PlayedStatuses = [BookingStatus.COMPLETED, BookingStatus.NO_SHOW, BookingStatus.PENDING_PAYMENT];

        private static readonly BookingStatus[] NoShowEligibleStatuses = [BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];

        // Trạng thái giữ sân theo luật cũ của job (sân về AVAILABLE khi không còn đơn nào ở các trạng thái này giữ)
        private static readonly BookingStatus[] CourtHoldingStatuses =
            [BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE, BookingStatus.IN_PROGRESS];

        // ── Hết hạn thanh toán (Job-01) và hết hạn duyệt ──────────────────────────────────────────────

        public async Task<List<Guid>> GetExpiredUnpaidIdsAsync(DateTime nowUtc, int take)
        {
            EnsureUtc(nowUtc, nameof(nowUtc));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

            // Chỉ đọc id; mỗi đơn được hủy trong transaction riêng và kiểm lại hạn dưới khóa
            return await _context.Invoices
                .AsNoTracking()
                .Where(i => i.PaymentStatus == InvoicePaymentStatus.UNPAID
                    && i.ExpiresAt <= nowUtc
                    && i.Booking.Status == BookingStatus.PENDING)
                .OrderBy(i => i.ExpiresAt)
                .ThenBy(i => i.BookingId)
                .Select(i => i.BookingId)
                .Take(take)
                .ToListAsync();
        }

        public async Task<List<Guid>> GetExpiredApprovalIdsAsync(DateTime nowUtc, int take)
        {
            EnsureUtc(nowUtc, nameof(nowUtc));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

            // Dùng chỉ mục idx_bookings_pending_approval (approval_expires_at, chỉ đơn PENDING_APPROVAL)
            return await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Status == BookingStatus.PENDING_APPROVAL && b.ApprovalExpiresAt <= nowUtc)
                .OrderBy(b => b.ApprovalExpiresAt)
                .ThenBy(b => b.Id)
                .Select(b => b.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<bool> IsPaymentWindowOverAsync(Guid bookingId, DateTime nowUtc)
        {
            EnsureInTransaction();
            EnsureUtc(nowUtc, nameof(nowUtc));

            // Cùng định nghĩa "hết hạn" với Thanh toán lại (expires_at <= now)
            return await _context.Bookings
                .AsNoTracking()
                .AnyAsync(b => b.Id == bookingId
                    && b.Status == BookingStatus.PENDING
                    && b.Invoice != null
                    && b.Invoice.PaymentStatus == InvoicePaymentStatus.UNPAID
                    && b.Invoice.ExpiresAt <= nowUtc);
        }

        public async Task<bool> IsApprovalWindowOverAsync(Guid bookingId, DateTime nowUtc)
        {
            EnsureInTransaction();
            EnsureUtc(nowUtc, nameof(nowUtc));

            // Phần bù của điều kiện duyệt / từ chối (approval_expires_at > now): hai bên không thể cùng thắng
            return await _context.Bookings
                .AsNoTracking()
                .AnyAsync(b => b.Id == bookingId
                    && b.Status == BookingStatus.PENDING_APPROVAL
                    && b.ApprovalExpiresAt <= nowUtc);
        }

        // ── Kết thúc giờ chơi (Job-02) và NO_SHOW (Job-04) ────────────────────────────────────────────

        public async Task<List<Booking>> GetNoShowCandidatesAsync(DateOnly todayVn)
        {
            return await WithPlayDetails(_context.Bookings.AsNoTracking())
                .Where(b => NoShowEligibleStatuses.Contains(b.Status)
                    && b.CheckedInAt == null
                    && b.BookingCourts.Any(bc => bc.Date <= todayVn))
                .ToListAsync();
        }

        public async Task<List<Booking>> GetPlayEndCandidatesAsync(DateOnly todayVn)
        {
            return await WithPlayDetails(_context.Bookings.AsNoTracking())
                .Where(b => (b.Status == BookingStatus.IN_PROGRESS
                        || (NoShowEligibleStatuses.Contains(b.Status) && b.CheckedInAt != null))
                    && b.BookingCourts.Any(bc => bc.Date <= todayVn))
                .ToListAsync();
        }

        private static IQueryable<Booking> WithPlayDetails(IQueryable<Booking> query) => query
            .Include(b => b.BookingCourts)
                .ThenInclude(bc => bc.Court)
            .Include(b => b.Invoice)
            .Include(b => b.Branch)
            .Include(b => b.Customer)
            .AsSplitQuery();

        public async Task<List<Guid>> GetCourtsHeldByOtherBookingsAsync(IReadOnlyCollection<Guid> courtIds, Guid excludeBookingId)
        {
            if (courtIds.Count == 0)
                return [];

            var ids = courtIds.ToArray();
            return await _context.BookingCourts
                .AsNoTracking()
                .Where(bc => ids.Contains(bc.CourtId)
                    && bc.IsActive
                    && bc.BookingId != excludeBookingId
                    && CourtHoldingStatuses.Contains(bc.Booking.Status))
                .Select(bc => bc.CourtId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<bool> MarkNoShowAsync(Guid bookingId, BookingStatus expectedStatus, DateTime nowUtc)
        {
            if (!NoShowEligibleStatuses.Contains(expectedStatus))
                throw new ArgumentOutOfRangeException(nameof(expectedStatus), expectedStatus, "Chỉ đơn CONFIRMED / PAID_ONLINE mới bị đánh NO_SHOW.");
            EnsureInTransaction();
            EnsureUtc(nowUtc, nameof(nowUtc));

            // Điều kiện checked_in_at IS NULL: lần check-in vừa commit (kể cả dữ liệu cũ còn CONFIRMED) thắng job
            return await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == expectedStatus && b.CheckedInAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.NO_SHOW)
                    .SetProperty(b => b.UpdatedAt, nowUtc)) == 1;
        }

        public async Task<bool> EndPlayAsync(Guid bookingId, BookingStatus expectedStatus, BookingStatus newStatus, DateTime nowUtc)
        {
            if (newStatus is not (BookingStatus.COMPLETED or BookingStatus.PENDING_PAYMENT))
                throw new ArgumentOutOfRangeException(nameof(newStatus), newStatus, "Hết giờ chơi chỉ chuyển sang COMPLETED hoặc PENDING_PAYMENT.");
            EnsureInTransaction();
            EnsureUtc(nowUtc, nameof(nowUtc));

            var won = await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == expectedStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, newStatus)
                    .SetProperty(b => b.UpdatedAt, nowUtc)) == 1;
            if (!won)
                return false;

            // Như Job-02 cũ: hóa đơn chỉ đổi updated_at, trạng thái thanh toán do check-out ghi
            await _context.Invoices
                .Where(i => i.BookingId == bookingId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, nowUtc));
            return true;
        }

        // ── Báo kết quả về TADSS ──────────────────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<Booking>> GetOutcomeCandidatesAsync(DateOnly todayVn, TimeOnly nowTimeVn, int take)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

            // Điều kiện của chỉ mục idx_bookings_outcome_pending (decision có, chưa báo) + sắp theo updated_at của chỉ mục.
            // Mapper kiểm lại giờ kết thúc và dữ liệu (checked_in_at) trước khi gửi
            return await _context.Bookings
                .AsNoTracking()
                .Include(b => b.BookingCourts)
                .Where(b => b.TadssDecisionId != null && b.TadssOutcomeReportedAt == null)
                .Where(b => CancelledStatuses.Contains(b.Status)
                    || (PlayedStatuses.Contains(b.Status)
                        && b.BookingCourts.All(bc => bc.Date < todayVn || (bc.Date == todayVn && bc.EndTime <= nowTimeVn))))
                .OrderByDescending(b => b.UpdatedAt)
                .ThenBy(b => b.Id)
                .Take(take)
                .ToListAsync();
        }

        public async Task<bool> MarkOutcomeReportedAsync(Guid bookingId, DateTime nowUtc)
        {
            EnsureUtc(nowUtc, nameof(nowUtc));

            return await _context.Bookings
                .Where(b => b.Id == bookingId && b.TadssOutcomeReportedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.TadssOutcomeReportedAt, (DateTime?)nowUtc)) == 1;
        }
    }
}
