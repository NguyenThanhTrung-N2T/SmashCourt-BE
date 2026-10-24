namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Lý do hủy booking ("vì sao hủy"); CancelSourceEnum giữ nghĩa "ai hủy"
    /// </summary>
    public enum BookingCancelReason
    {
        /// <summary>Hết hạn thanh toán</summary>
        PAYMENT_EXPIRED = 0,

        /// <summary>Manager không duyệt kịp</summary>
        APPROVAL_EXPIRED = 1,

        /// <summary>Manager từ chối</summary>
        APPROVAL_REJECTED = 2,

        /// <summary>TADSS từ chối lúc đặt</summary>
        POLICY_REJECTED = 3,

        /// <summary>Khách hủy trong app hoặc bằng link</summary>
        CUSTOMER_CANCELLED = 4,

        /// <summary>Nhân viên hủy</summary>
        STAFF_CANCELLED = 5,

        /// <summary>Dọn đơn dở dang (BE dừng giữa hai transaction)</summary>
        SYSTEM_CLEANUP = 6
    }
}
