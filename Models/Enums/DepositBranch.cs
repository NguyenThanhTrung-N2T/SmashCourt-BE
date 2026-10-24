namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Nhánh xử lý đơn online sau khi có quyết định cọc. Chỉ dùng trong BE, không lưu DB.
    /// </summary>
    public enum DepositBranch
    {
        TadssDeposit = 0,                 // TADSS duyệt, cọc đủ mức tối thiểu: thu cọc qua VNPay
        ConfirmWithoutOnlinePayment = 1,  // Số tiền cần thu online dưới mức tối thiểu: xác nhận, thu tại quầy
        NeedApproval = 2,                 // Chờ Quản lý duyệt
        Rejected = 3,                     // TADSS từ chối: hủy đơn
        FullPaymentFallback = 4           // Cờ tắt hoặc TADSS lỗi: thu 100 % qua VNPay
    }
}
