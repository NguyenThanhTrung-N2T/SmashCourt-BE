using System.Data;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    /// <summary>Câu đọc chỉ đọc cho kiểm tính đúng dữ liệu đặt sân; luật kiểm nằm ở <c>Services/Invariants/BookingInvariantRules</c>.</summary>
    public class BookingInvariantRepository : IBookingInvariantRepository
    {
        private readonly SmashCourtContext _context;

        public BookingInvariantRepository(SmashCourtContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Chạy <paramref name="read"/> trong một transaction REPEATABLE READ (PostgreSQL: một snapshot từ câu đọc đầu tiên), rồi rollback vì chỉ đọc.
        /// DbContext đã có transaction (test rollback, lượt kiểm staging) thì đọc trong transaction đó, không mở transaction mới.
        /// </summary>
        public static async Task<T> InSnapshotAsync<T>(SmashCourtContext db, Func<Task<T>> read, CancellationToken ct = default)
        {
            if (db.Database.CurrentTransaction != null)
                return await read();

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var result = await read();
            await transaction.RollbackAsync(ct);
            return result;
        }

        public Task<BookingInvariantSnapshot> LoadAsync(BookingScope scope, CancellationToken ct = default) =>
            InSnapshotAsync(_context, () => LoadInSnapshotAsync(scope, ct), ct);

        public async Task<List<Guid>> ResolveScopeAsync(BookingScope scope, CancellationToken ct = default)
        {
            if (scope.BookingIds != null)
                return scope.BookingIds.Distinct().ToList();
            if (scope is not { FromUtc: { } from, ToUtc: { } to })
                throw new ArgumentException("Phạm vi phải có danh sách id hoặc đủ FromUtc và ToUtc.", nameof(scope));

            var active = BookingStatusTransition.GetActiveStatuses();
            var includeActive = scope.IncludeActive;
            var query = _context.Bookings.AsNoTracking()
                .Where(b => (b.CreatedAt >= from && b.CreatedAt <= to) || (b.UpdatedAt >= from && b.UpdatedAt <= to)
                            || (includeActive && active.Contains(b.Status)));
            if (scope.SettledBeforeUtc is { } settled)
                query = query.Where(b => b.UpdatedAt <= settled && b.CreatedAt <= settled);
            return await query.Select(b => b.Id).ToListAsync(ct);
        }

        private async Task<BookingInvariantSnapshot> LoadInSnapshotAsync(BookingScope scope, CancellationToken ct)
        {
            var ids = await ResolveScopeAsync(scope, ct);
            if (ids.Count == 0)
                return BookingInvariantSnapshot.Empty;

            var bookings = await _context.Bookings.AsNoTracking()
                .Where(b => ids.Contains(b.Id))
                .Select(b => new InvariantBookingRow(
                    b.Id, b.Status, b.ApprovalExpiresAt, b.CancelReason, b.CancelSource, b.TadssDecisionId, b.CustomerId, b.BookingCode))
                .ToListAsync(ct);
            var invoices = await _context.Invoices.AsNoTracking()
                .Where(i => ids.Contains(i.BookingId))
                .Select(i => new InvariantInvoiceRow(
                    i.BookingId,
                    i.DepositSource,
                    i.DepositAmount,
                    i.HoldDeadline,
                    i.PaymentStatus,
                    i.FinalTotal,
                    // Chỉ Payment SUCCESS của chính hóa đơn này
                    i.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount),
                    // Thực thu, cùng công thức với RecalculatePaymentStatusAsync của BE
                    i.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount - p.RefundedAmount)))
                .ToDictionaryAsync(i => i.BookingId, ct);
            var lines = await _context.BookingCourts.AsNoTracking()
                .Where(bc => ids.Contains(bc.BookingId))
                .Select(bc => new InvariantCourtLine(bc.Id, bc.BookingId, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime, bc.IsActive))
                .ToListAsync(ct);
            var locks = await _context.SlotLocks.AsNoTracking()
                .Where(l => ids.Contains(l.BookingId))
                .Select(l => new InvariantLockRow(l.BookingId, l.CourtId, l.Date, l.StartTime, l.EndTime, l.ExpiresAt))
                .ToListAsync(ct);
            var holding = await HoldingLinesNearAsync(lines, ct);

            return new BookingInvariantSnapshot(ids, bookings, invoices, lines, locks, holding);
        }

        // Mọi dòng đang giữ sân (của bất kỳ đơn nào) trên cùng sân, cùng ngày với dòng sân của các đơn trong phạm vi. "Đang giữ sân" = đơn hiệu lực,
        // không lọc IsActive (để bắt hậu quả của dòng sân bị tắt sai), trừ dòng đã tắt của đơn được phép nhả sân khi còn hiệu lực
        private async Task<List<InvariantCourtLine>> HoldingLinesNearAsync(IReadOnlyCollection<InvariantCourtLine> scopedLines, CancellationToken ct)
        {
            if (scopedLines.Count == 0)
                return [];

            var courtIds = scopedLines.Select(l => l.CourtId).Distinct().ToList();
            var dates = scopedLines.Select(l => l.Date).Distinct().ToList();
            var active = BookingStatusTransition.GetActiveStatuses();
            var released = InvariantRules.CourtsReleasedWhileActive;

            var rows = await _context.BookingCourts.AsNoTracking()
                .Where(bc => courtIds.Contains(bc.CourtId) && dates.Contains(bc.Date)
                             && active.Contains(bc.Booking.Status)
                             && (bc.IsActive || !released.Contains(bc.Booking.Status)))
                .Select(bc => new InvariantCourtLine(bc.Id, bc.BookingId, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime, bc.IsActive))
                .ToListAsync(ct);

            // Câu đọc lọc riêng theo sân và theo ngày nên lấy cả tích chéo; chỉ giữ cặp (sân, ngày) có dòng của đơn trong phạm vi
            var pairs = scopedLines.Select(l => (l.CourtId, l.Date)).ToHashSet();
            return rows.Where(l => pairs.Contains((l.CourtId, l.Date))).ToList();
        }
    }
}
