using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class CustomerHistoryRepository : ICustomerHistoryRepository
    {
        // Chỉ đơn đã kết thúc (hoàn tất, không đến) hoặc đã hủy mới có thể vào lịch sử
        private static readonly BookingStatus[] HistoryStatuses =
            [.. BookingStatusTransition.GetCancelledStatuses(), BookingStatus.COMPLETED, BookingStatus.NO_SHOW];

        private readonly SmashCourtContext _db;

        public CustomerHistoryRepository(SmashCourtContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<CustomerHistoryEntry>> GetEntriesAsync(
            Guid customerId, Guid excludeBookingId, CancellationToken cancellationToken = default)
        {
            // Một truy vấn chỉ lấy cột cần. Sân dùng mọi dòng booking_courts (kể cả không hoạt động) như bản export Phase 5
            var rows = await _db.Bookings
                .AsNoTracking()
                .Where(b => b.CustomerId == customerId && b.Id != excludeBookingId && HistoryStatuses.Contains(b.Status))
                .Select(b => new
                {
                    b.Status,
                    b.CancelReason,
                    b.CancelledAt,
                    Courts = b.BookingCourts.Select(c => new { c.Date, c.EndTime }).ToList(),
                    HasSuccessfulPayment = b.Invoice != null && b.Invoice.Payments.Any(
                        p => p.Status == PaymentTxStatus.SUCCESS || p.Status == PaymentTxStatus.REFUNDED),
                    MaxRefundPercent = b.Invoice == null
                        ? null
                        : b.Invoice.Payments.SelectMany(p => p.Refunds).Max(r => (decimal?)r.RefundPercent)
                })
                .ToListAsync(cancellationToken);

            return rows.Select(row =>
            {
                // Sân kết thúc muộn nhất = lớn nhất theo (ngày, giờ kết thúc)
                var last = row.Courts.OrderByDescending(c => c.Date).ThenByDescending(c => c.EndTime)
                    .Select(c => ((DateOnly Date, TimeOnly EndTime)?)(c.Date, c.EndTime))
                    .FirstOrDefault();
                return new CustomerHistoryEntry(
                    row.Status, row.CancelReason, row.CancelledAt, last?.Date, last?.EndTime,
                    row.HasSuccessfulPayment, row.MaxRefundPercent);
            }).ToList();
        }
    }
}
