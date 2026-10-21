namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Nhánh xử lý đơn online sau khi có quyết định cọc (spec Phase 6, 7.6). Chỉ dùng trong BE, không lưu DB.
    /// </summary>
    public enum DepositBranch
    {
        /// <summary>(a) TADSS APPROVED, số tiền cọc đủ mức tối thiểu: thu cọc qua VNPay</summary>
        TadssDeposit = 0,

        /// <summary>(a′) Số tiền cần thu online dưới mức tối thiểu: xác nhận ngay, thu tại quầy</summary>
        ConfirmWithoutOnlinePayment = 1,

        /// <summary>(b) TADSS NEED_APPROVAL: chờ Manager duyệt</summary>
        NeedApproval = 2,

        /// <summary>(c) TADSS REJECTED: hủy đơn</summary>
        Rejected = 3,

        /// <summary>(d) Tắt cờ hoặc TADSS lỗi: thu 100% qua VNPay</summary>
        FullPaymentFallback = 4
    }
}
