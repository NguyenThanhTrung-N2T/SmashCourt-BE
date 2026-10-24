using System.Transactions;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// <c>TransactionScope</c> cho mã ghi dữ liệu. Mặc định của <c>new TransactionScope()</c> là <b>Serializable</b>: hai đơn ghi cùng lúc
    /// (kể cả khác sân, khác chi nhánh) có thể làm một bên lỗi <c>40001</c> lúc commit ⇒ 500 (N-29, phát hiện 23/10 bằng BE-IT đồng thời).
    /// <para>
    /// Chỉ dùng cho luồng đã thiết kế cho Read Committed: không trùng sân nhờ exclusion constraint của DB, đổi trạng thái bằng câu cổng
    /// có điều kiện (<c>ExecuteUpdate ... WHERE status = ...</c>), mã đơn/hóa đơn lấy từ sequence. Đường cũ còn đọc rồi ghi không điều kiện
    /// (hủy, IPN, thanh toán lại) giữ mặc định tới khi 6A-4 viết lại, vì hạ mức cô lập ở đó sẽ thành mất cập nhật thay vì lỗi.
    /// </para>
    /// </summary>
    public static class TransactionScopeHelper
    {
        public static TransactionScope ReadCommitted() => new(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TransactionManager.DefaultTimeout },
            TransactionScopeAsyncFlowOption.Enabled);
    }
}
