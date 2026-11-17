using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Invariants
{
    /// <summary>Tên các điều kiểm tính đúng dữ liệu đặt sân; giá trị dùng làm nhãn metric, trong log và trong báo cáo JSON của lượt kiểm staging.</summary>
    public static class InvariantRules
    {
        public const string ConfirmedIsPaid = "SC6-1";
        public const string AwaitingApprovalHoldsLocks = "SC6-2";
        public const string CancelledHoldsNothing = "SC6-3";
        public const string ActiveCourtsAreActive = "SC6-4a";
        public const string NoDoubleBooking = "SC6-4b";
        public const string DecisionExistsInTadss = "SC6-5"; // chỉ ở lượt kiểm staging (gọi TADSS), không có trong kiểm định kỳ
        public const string CancelledHasReason = "SC6-6";
        public const string PaidInvoiceIsCollected = "SC6-7";
        public const string PendingHasHoldDeadline = "PHU-2";

        /// <summary>
        /// Trạng thái hiệu lực mà dòng sân được phép tắt: job kết thúc giờ chơi cố ý nhả sân khi đơn chưa thanh toán đủ (đơn vẫn chờ thu tiền).
        /// Khai báo một lần ở đây; test buộc tập này là tập con của các trạng thái hiệu lực.
        /// </summary>
        public static readonly BookingStatus[] CourtsReleasedWhileActive = [BookingStatus.PENDING_PAYMENT];

        /// <summary>Các điều kiểm chỉ bằng DB của BE (mọi điều trừ <see cref="DecisionExistsInTadss"/>), theo thứ tự tên.</summary>
        public static readonly IReadOnlyList<string> DatabaseRules =
        [
            PendingHasHoldDeadline, ConfirmedIsPaid, AwaitingApprovalHoldsLocks, CancelledHoldsNothing,
            ActiveCourtsAreActive, NoDoubleBooking, CancelledHasReason, PaidInvoiceIsCollected
        ];
    }
}
