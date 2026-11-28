using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Dữ liệu của các đơn trong phạm vi kiểm tính đúng, đọc trong một snapshot của DB: đơn, hóa đơn, dòng sân, slot lock, và mọi dòng đang giữ
    /// sân trên cùng sân, cùng ngày (của bất kỳ đơn nào) để kiểm trùng giờ.
    /// </summary>
    public sealed record BookingInvariantSnapshot(
        IReadOnlyList<Guid> ScopedBookingIds,
        IReadOnlyList<InvariantBookingRow> Bookings,
        IReadOnlyDictionary<Guid, InvariantInvoiceRow> InvoicesByBooking,
        IReadOnlyList<InvariantCourtLine> Lines,
        IReadOnlyList<InvariantLockRow> Locks,
        IReadOnlyList<InvariantCourtLine> HoldingLines)
    {
        public static readonly BookingInvariantSnapshot Empty = new([], [], new Dictionary<Guid, InvariantInvoiceRow>(), [], [], []);
    }

    public sealed record InvariantBookingRow(
        Guid Id, BookingStatus Status, DateTime? ApprovalExpiresAt, BookingCancelReason? CancelReason, CancelSourceEnum? CancelSource,
        Guid? TadssDecisionId, Guid? CustomerId, string BookingCode);

    /// <summary>Paid: tổng Payment SUCCESS của hóa đơn; Collected: thực thu (SUCCESS trừ đã hoàn), cùng công thức tính trạng thái thanh toán của BE.</summary>
    public sealed record InvariantInvoiceRow(
        Guid BookingId, DepositSource? DepositSource, decimal? DepositAmount, DateTime? HoldDeadline, InvoicePaymentStatus PaymentStatus,
        decimal FinalTotal, decimal Paid, decimal Collected);

    public sealed record InvariantCourtLine(Guid Id, Guid BookingId, Guid CourtId, DateOnly Date, TimeOnly Start, TimeOnly End, bool IsActive)
    {
        public override string ToString() =>
            $"sân {CourtId} {Date:yyyy-MM-dd} {Start:HH\\:mm}–{End:HH\\:mm}{(IsActive ? "" : " (đã tắt)")}";
    }

    public sealed record InvariantLockRow(Guid BookingId, Guid CourtId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, DateTime ExpiresAt);
}
