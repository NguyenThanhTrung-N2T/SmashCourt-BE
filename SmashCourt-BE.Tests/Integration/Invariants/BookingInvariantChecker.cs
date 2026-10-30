using System.Data;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// Bộ kiểm SC6 "0 booking hỏng" (BE-IT-20, P6-29; plan 6A-6 Task 9) viết bằng LINQ chỉ đọc trên đúng mô hình của BE, nên tập trạng thái lấy từ
/// <see cref="BookingStatusTransition"/> (một nguồn, không chép ra file SQL). Dùng chung cho test (phạm vi theo id) và lượt kiểm staging
/// (phạm vi theo thời gian). Mọi câu đọc trong một snapshot REPEATABLE READ nên các điều so với nhau trên cùng một trạng thái DB.
/// </summary>
public static class BookingInvariantChecker
{
    // P6-25: Job-02 cố ý nhả sân (tắt booking_courts) khi hết giờ chơi mà đơn chưa thanh toán đủ; đơn vẫn PENDING_PAYMENT (hiệu lực).
    // Khai báo một lần ở đây; test buộc tập này là tập con của GetActiveStatuses()
    public static readonly BookingStatus[] CourtsReleasedWhileActive = [BookingStatus.PENDING_PAYMENT];

    private static readonly BookingStatus[] PaidStatuses = [BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];

    public static Task<IReadOnlyList<InvariantViolation>> CheckAsync(
        SmashCourtContext db, BookingScope scope, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(scope);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc phải có Kind = Utc.", nameof(nowUtc));
        ValidateScope(scope);

        return InSnapshotAsync(db, () => CheckInSnapshotAsync(db, scope, nowUtc, ct), ct);
    }

    /// <summary>
    /// Chạy <paramref name="read"/> trong một transaction REPEATABLE READ (PostgreSQL: một snapshot từ câu đọc đầu tiên), rồi rollback vì chỉ đọc.
    /// DbContext đã có transaction (test rollback) thì đọc trong transaction đó, không mở transaction mới.
    /// </summary>
    internal static async Task<T> InSnapshotAsync<T>(SmashCourtContext db, Func<Task<T>> read, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction != null)
            return await read();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var result = await read();
        await transaction.RollbackAsync(ct);
        return result;
    }

    private static void ValidateScope(BookingScope scope)
    {
        if (scope.BookingIds != null)
            return;
        if (scope.FromUtc is not { } from || scope.ToUtc is not { } to)
            throw new ArgumentException("Phạm vi phải có danh sách id hoặc đủ FromUtc và ToUtc.", nameof(scope));
        if (from.Kind != DateTimeKind.Utc || to.Kind != DateTimeKind.Utc)
            throw new ArgumentException("FromUtc, ToUtc phải có Kind = Utc.", nameof(scope));
        if (from > to)
            throw new ArgumentException("FromUtc phải trước hoặc bằng ToUtc.", nameof(scope));
    }

    private static async Task<IReadOnlyList<InvariantViolation>> CheckInSnapshotAsync(
        SmashCourtContext db, BookingScope scope, DateTime now, CancellationToken ct)
    {
        var ids = await ResolveScopeAsync(db, scope, ct);
        if (ids.Count == 0)
            return [];

        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Status, b.ApprovalExpiresAt, b.CancelReason, b.CancelSource, b.TadssDecisionId, b.CustomerId })
            .ToListAsync(ct);
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => ids.Contains(i.BookingId))
            .Select(i => new
            {
                i.BookingId,
                i.DepositSource,
                i.DepositAmount,
                i.HoldDeadline,
                // Chỉ Payment SUCCESS của chính hóa đơn này
                Paid = i.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount)
            })
            .ToDictionaryAsync(i => i.BookingId, ct);
        var lines = await db.BookingCourts.AsNoTracking()
            .Where(bc => ids.Contains(bc.BookingId))
            .Select(bc => new CourtLine(bc.Id, bc.BookingId, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime, bc.IsActive))
            .ToListAsync(ct);
        var locks = await db.SlotLocks.AsNoTracking()
            .Where(l => ids.Contains(l.BookingId))
            .Select(l => new { l.BookingId, l.CourtId, l.Date, l.StartTime, l.EndTime, l.ExpiresAt })
            .ToListAsync(ct);
        var holding = await HoldingLinesNearAsync(db, lines, ct);

        var active = BookingStatusTransition.GetActiveStatuses();
        var cancelled = BookingStatusTransition.GetCancelledStatuses();
        var linesByBooking = lines.ToLookup(l => l.BookingId);
        var locksByBooking = locks.ToLookup(l => l.BookingId);
        var violations = new List<InvariantViolation>();

        foreach (var booking in bookings)
        {
            invoices.TryGetValue(booking.Id, out var invoice);
            var bookingLines = linesByBooking[booking.Id].ToList();
            var bookingLocks = locksByBooking[booking.Id].ToList();
            void Report(string rule, string detail) => violations.Add(new InvariantViolation(rule, booking.Id, detail));

            // SC6-1: đơn đã xác nhận qua luồng cọc phải có tiền SUCCESS của đúng hóa đơn đủ mức cọc
            if (PaidStatuses.Contains(booking.Status) && invoice?.DepositSource != null
                && invoice.Paid < (invoice.DepositAmount ?? 0m))
                Report(InvariantRules.ConfirmedIsPaid,
                    $"{booking.Status}, cọc {invoice.DepositAmount:0} đ nhưng Payment SUCCESS của hóa đơn chỉ {invoice.Paid:0} đ");

            // SC6-2: đơn còn trong hạn duyệt phải giữ slot lock còn hạn khớp từng dòng sân
            if (booking.Status == BookingStatus.PENDING_APPROVAL && booking.ApprovalExpiresAt > now)
            {
                foreach (var line in bookingLines.Where(line => !bookingLocks.Any(l =>
                             l.CourtId == line.CourtId && l.Date == line.Date && l.StartTime == line.Start
                             && l.EndTime == line.End && l.ExpiresAt > now)))
                    Report(InvariantRules.AwaitingApprovalHoldsLocks,
                        $"chờ duyệt tới {booking.ApprovalExpiresAt:O} nhưng thiếu slot lock còn hạn cho {line}");
            }

            if (cancelled.Contains(booking.Status))
            {
                // SC6-3: đơn đã hủy không còn giữ gì
                foreach (var l in bookingLocks.Where(l => l.ExpiresAt > now))
                    Report(InvariantRules.CancelledHoldsNothing,
                        $"{booking.Status} còn slot lock sân {l.CourtId} {l.Date:yyyy-MM-dd} {l.StartTime:HH\\:mm}–{l.EndTime:HH\\:mm} hạn {l.ExpiresAt:O}");
                foreach (var line in bookingLines.Where(l => l.IsActive))
                    Report(InvariantRules.CancelledHoldsNothing, $"{booking.Status} còn dòng sân đang bật {line}");

                // SC6-6: DB không bắt buộc (chk_bookings_cancel_reason chỉ cấm lý do ở đơn chưa hủy) nên kiểm ở đây. Mô tả ghi mức ảnh hưởng
                // theo nơi đọc lý do và cancel_source (gợi ý lý do đúng ra là gì)
                if (booking.CancelReason == null)
                    Report(InvariantRules.CancelledHasReason,
                        $"{booking.Status} không có cancel_reason (cancel_source {booking.CancelSource?.ToString() ?? "trống"}) — " +
                        CancelReasonImpact(booking.TadssDecisionId != null, booking.CustomerId != null));
            }

            // SC6-4a: đơn hiệu lực (trừ ngoại lệ nhả sân) phải bật mọi dòng sân
            if (active.Contains(booking.Status) && !CourtsReleasedWhileActive.Contains(booking.Status))
            {
                foreach (var line in bookingLines.Where(l => !l.IsActive))
                    Report(InvariantRules.ActiveCourtsAreActive, $"{booking.Status} có dòng sân bị tắt {line}");
            }

            // Phụ-2: đơn chờ thanh toán (từ Phase 6) luôn có hạn giữ chỗ tuyệt đối
            if (booking.Status == BookingStatus.PENDING && invoice?.HoldDeadline == null)
                Report(InvariantRules.PendingHasHoldDeadline,
                    invoice == null ? "PENDING không có hóa đơn" : "PENDING có hóa đơn thiếu hold_deadline");
        }

        violations.AddRange(DoubleBookings(holding, ids.ToHashSet()));

        return violations
            .OrderBy(v => v.Rule, StringComparer.Ordinal)
            .ThenBy(v => v.BookingId)
            .ThenBy(v => v.Detail, StringComparer.Ordinal)
            .ToList();
    }

    // Đơn hủy thiếu lý do: TadssOutcomeMapper trả Invalid nên outcome không bao giờ gửi (đơn có quyết định); CustomerHistoryCalculator
    // không đếm lần hủy đó vào lịch sử gửi TADSS (đơn có khách); còn lại chỉ thiếu lý do khi hiển thị (spec P6-29)
    internal static string CancelReasonImpact(bool hasDecision, bool hasCustomer) => (hasDecision, hasCustomer) switch
    {
        (true, true) => "CRITICAL: outcome về TADSS không gửi được ([Outcome] Invalid mỗi chu kỳ) và lịch sử khách gửi TADSS bỏ sót lần hủy này",
        (true, false) => "CRITICAL: outcome về TADSS không gửi được ([Outcome] Invalid mỗi chu kỳ)",
        (false, true) => "HIGH: lịch sử khách gửi TADSS bỏ sót lần hủy này (khách trông đáng tin hơn thực tế)",
        _ => "LOW: khách vãng lai, không có quyết định TADSS; chỉ không hiện lý do hủy"
    };

    private static async Task<List<Guid>> ResolveScopeAsync(SmashCourtContext db, BookingScope scope, CancellationToken ct)
    {
        if (scope.BookingIds != null)
            return scope.BookingIds.Distinct().ToList();
        if (scope is not { FromUtc: { } from, ToUtc: { } to })
            throw new ArgumentException("Phạm vi phải có danh sách id hoặc đủ FromUtc và ToUtc.", nameof(scope));

        return await db.Bookings.AsNoTracking()
            .Where(b => (b.CreatedAt >= from && b.CreatedAt <= to) || (b.UpdatedAt >= from && b.UpdatedAt <= to))
            .Select(b => b.Id)
            .ToListAsync(ct);
    }

    // Mọi dòng đang giữ sân (của bất kỳ đơn nào) trên cùng sân, cùng ngày với dòng sân của các đơn trong phạm vi. "Đang giữ sân" = đơn hiệu lực,
    // không lọc IsActive (để bắt hậu quả của SC6-4a), trừ dòng đã tắt của đơn thuộc CourtsReleasedWhileActive
    private static async Task<List<CourtLine>> HoldingLinesNearAsync(
        SmashCourtContext db, IReadOnlyCollection<CourtLine> scopedLines, CancellationToken ct)
    {
        if (scopedLines.Count == 0)
            return [];

        var courtIds = scopedLines.Select(l => l.CourtId).Distinct().ToList();
        var dates = scopedLines.Select(l => l.Date).Distinct().ToList();
        var active = BookingStatusTransition.GetActiveStatuses();
        var released = CourtsReleasedWhileActive;

        return await db.BookingCourts.AsNoTracking()
            .Where(bc => courtIds.Contains(bc.CourtId) && dates.Contains(bc.Date)
                         && active.Contains(bc.Booking.Status)
                         && (bc.IsActive || !released.Contains(bc.Booking.Status)))
            .Select(bc => new CourtLine(bc.Id, bc.BookingId, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime, bc.IsActive))
            .ToListAsync(ct);
    }

    // SC6-4b: hai đơn khác nhau giữ cùng sân, cùng ngày, giờ chồng theo nửa khoảng; ít nhất một đơn trong phạm vi; mỗi cặp dòng báo một lần,
    // gắn cho đơn trong phạm vi (cả hai trong phạm vi thì đơn có id nhỏ hơn)
    private static IEnumerable<InvariantViolation> DoubleBookings(IReadOnlyList<CourtLine> holding, HashSet<Guid> scope)
    {
        foreach (var group in holding.GroupBy(l => (l.CourtId, l.Date)))
        {
            var groupLines = group.OrderBy(l => l.Id).ToList();
            for (var i = 0; i < groupLines.Count; i++)
            for (var j = i + 1; j < groupLines.Count; j++)
            {
                var a = groupLines[i];
                var b = groupLines[j];
                if (a.BookingId == b.BookingId || !(a.Start < b.End && b.Start < a.End))
                    continue;
                if (!scope.Contains(a.BookingId) && !scope.Contains(b.BookingId))
                    continue;

                var owner = scope.Contains(a.BookingId) && scope.Contains(b.BookingId)
                    ? (a.BookingId.CompareTo(b.BookingId) < 0 ? a.BookingId : b.BookingId)
                    : scope.Contains(a.BookingId) ? a.BookingId : b.BookingId;
                yield return new InvariantViolation(InvariantRules.NoDoubleBooking, owner,
                    $"đơn {a.BookingId} và {b.BookingId} cùng giữ {a} / {b}");
            }
        }
    }

    private sealed record CourtLine(Guid Id, Guid BookingId, Guid CourtId, DateOnly Date, TimeOnly Start, TimeOnly End, bool IsActive)
    {
        public override string ToString() =>
            $"sân {CourtId} {Date:yyyy-MM-dd} {Start:HH\\:mm}–{End:HH\\:mm}{(IsActive ? "" : " (đã tắt)")}";
    }
}
