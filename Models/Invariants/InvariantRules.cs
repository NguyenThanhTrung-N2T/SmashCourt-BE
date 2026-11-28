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

        /// <summary>
        /// Tên dễ hiểu của từng điều, cho người vận hành đọc trên dashboard, trong log và email; nơi duy nhất khai báo
        /// (dashboard lấy qua nhãn metric rule_name, không chép lại).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ConfirmedIsPaid] = "Đơn đã xác nhận nhưng chưa thu đủ tiền cọc",
            [AwaitingApprovalHoldsLocks] = "Đơn chờ duyệt không còn giữ sân",
            [CancelledHoldsNothing] = "Đơn đã hủy vẫn còn giữ sân",
            [ActiveCourtsAreActive] = "Đơn còn hiệu lực nhưng có sân bị tắt",
            [NoDoubleBooking] = "Hai đơn trùng sân, trùng giờ",
            [DecisionExistsInTadss] = "Đơn không có quyết định cọc trên TADSS",
            [CancelledHasReason] = "Đơn đã hủy không có lý do hủy",
            [PaidInvoiceIsCollected] = "Hóa đơn ghi đã trả nhưng thu chưa đủ tiền",
            [PendingHasHoldDeadline] = "Đơn chờ thanh toán không có hạn giữ chỗ",
        };

        public static string Title(string rule) => Titles.TryGetValue(rule, out var title) ? title : rule;
    }
}
