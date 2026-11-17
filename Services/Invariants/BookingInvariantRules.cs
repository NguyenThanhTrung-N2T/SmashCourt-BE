using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Services.Invariants;

/// <summary>
/// Các điều "0 booking hỏng" trên dữ liệu đã đọc trong một snapshot. Hàm thuần: nhận "bây giờ" làm tham số (UTC); tập trạng thái lấy từ
/// <see cref="BookingStatusTransition"/> nên không chép ra nơi khác. Dùng chung cho test, lượt kiểm staging và job kiểm định kỳ trên bản deploy.
/// </summary>
public static class BookingInvariantRules
{
    private static readonly BookingStatus[] PaidStatuses = [BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];

    // Vi phạm sắp theo (điều, đơn, mô tả) để kết quả tất định
    public static IReadOnlyList<InvariantViolation> Evaluate(BookingInvariantSnapshot snapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc phải có Kind = Utc.", nameof(nowUtc));
        if (snapshot.ScopedBookingIds.Count == 0)
            return [];

        var now = nowUtc;
        var active = BookingStatusTransition.GetActiveStatuses();
        var cancelled = BookingStatusTransition.GetCancelledStatuses();
        var linesByBooking = snapshot.Lines.ToLookup(l => l.BookingId);
        var locksByBooking = snapshot.Locks.ToLookup(l => l.BookingId);
        var violations = new List<InvariantViolation>();

        foreach (var booking in snapshot.Bookings)
        {
            snapshot.InvoicesByBooking.TryGetValue(booking.Id, out var invoice);
            var bookingLines = linesByBooking[booking.Id].ToList();
            var bookingLocks = locksByBooking[booking.Id].ToList();
            void Report(string rule, string detail) => violations.Add(new InvariantViolation(rule, booking.Id, detail));

            // Đơn đã xác nhận qua luồng cọc phải có tiền SUCCESS của đúng hóa đơn đủ mức cọc
            if (PaidStatuses.Contains(booking.Status) && invoice?.DepositSource != null
                && invoice.Paid < (invoice.DepositAmount ?? 0m))
                Report(InvariantRules.ConfirmedIsPaid,
                    $"{booking.Status}, cọc {invoice.DepositAmount:0} đ nhưng Payment SUCCESS của hóa đơn chỉ {invoice.Paid:0} đ");

            // Hóa đơn PAID phải thu đủ FinalTotal; thu thừa không vi phạm (khoản thêm được hoàn tại chi nhánh)
            if (invoice is { PaymentStatus: InvoicePaymentStatus.PAID } && invoice.Collected < invoice.FinalTotal)
                Report(InvariantRules.PaidInvoiceIsCollected,
                    $"hóa đơn PAID tổng {invoice.FinalTotal:0} đ nhưng thực thu (SUCCESS − đã hoàn) chỉ {invoice.Collected:0} đ");

            // Đơn còn trong hạn duyệt phải giữ slot lock còn hạn khớp từng dòng sân
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
                // Đơn đã hủy không còn giữ gì
                foreach (var l in bookingLocks.Where(l => l.ExpiresAt > now))
                    Report(InvariantRules.CancelledHoldsNothing,
                        $"{booking.Status} còn slot lock sân {l.CourtId} {l.Date:yyyy-MM-dd} {l.StartTime:HH\\:mm}–{l.EndTime:HH\\:mm} hạn {l.ExpiresAt:O}");
                foreach (var line in bookingLines.Where(l => l.IsActive))
                    Report(InvariantRules.CancelledHoldsNothing, $"{booking.Status} còn dòng sân đang bật {line}");

                // DB không bắt buộc (ràng buộc chỉ cấm lý do ở đơn chưa hủy) nên kiểm ở đây. Mô tả ghi mức ảnh hưởng theo nơi đọc lý do
                // và cancel_source (gợi ý lý do đúng ra là gì)
                if (booking.CancelReason == null)
                    Report(InvariantRules.CancelledHasReason,
                        $"{booking.Status} không có cancel_reason (cancel_source {booking.CancelSource?.ToString() ?? "trống"}) — " +
                        CancelReasonImpact(booking.TadssDecisionId != null, booking.CustomerId != null));
            }

            // Đơn hiệu lực (trừ ngoại lệ nhả sân) phải bật mọi dòng sân
            if (active.Contains(booking.Status) && !InvariantRules.CourtsReleasedWhileActive.Contains(booking.Status))
            {
                foreach (var line in bookingLines.Where(l => !l.IsActive))
                    Report(InvariantRules.ActiveCourtsAreActive, $"{booking.Status} có dòng sân bị tắt {line}");
            }

            // Đơn chờ thanh toán luôn có hạn giữ chỗ tuyệt đối
            if (booking.Status == BookingStatus.PENDING && invoice?.HoldDeadline == null)
                Report(InvariantRules.PendingHasHoldDeadline,
                    invoice == null ? "PENDING không có hóa đơn" : "PENDING có hóa đơn thiếu hold_deadline");
        }

        violations.AddRange(DoubleBookings(snapshot.HoldingLines, snapshot.ScopedBookingIds.ToHashSet()));

        return violations
            .OrderBy(v => v.Rule, StringComparer.Ordinal)
            .ThenBy(v => v.BookingId)
            .ThenBy(v => v.Detail, StringComparer.Ordinal)
            .ToList();
    }

    // Đơn hủy thiếu lý do: outcome về TADSS không bao giờ gửi (đơn có quyết định); lịch sử khách gửi TADSS không đếm lần hủy đó (đơn có khách);
    // còn lại chỉ thiếu lý do khi hiển thị
    public static string CancelReasonImpact(bool hasDecision, bool hasCustomer) => (hasDecision, hasCustomer) switch
    {
        (true, true) => "CRITICAL: outcome về TADSS không gửi được ([Outcome] Invalid mỗi chu kỳ) và lịch sử khách gửi TADSS bỏ sót lần hủy này",
        (true, false) => "CRITICAL: outcome về TADSS không gửi được ([Outcome] Invalid mỗi chu kỳ)",
        (false, true) => "HIGH: lịch sử khách gửi TADSS bỏ sót lần hủy này (khách trông đáng tin hơn thực tế)",
        _ => "LOW: khách vãng lai, không có quyết định TADSS; chỉ không hiện lý do hủy"
    };

    // Hai đơn khác nhau giữ cùng sân, cùng ngày, giờ chồng theo nửa khoảng; ít nhất một đơn trong phạm vi; mỗi cặp dòng báo một lần,
    // gắn cho đơn trong phạm vi (cả hai trong phạm vi thì đơn có id nhỏ hơn)
    private static IEnumerable<InvariantViolation> DoubleBookings(IReadOnlyList<InvariantCourtLine> holding, HashSet<Guid> scope)
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
}
