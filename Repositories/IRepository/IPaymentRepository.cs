using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IPaymentRepository
    {
        Task<Payment?> GetByTransactionRefAsync(string transactionRef);

        /// <summary>
        /// Lấy payment theo transaction ref với option AsNoTracking
        /// Dùng cho idempotency check để tránh EF Core tracking conflict
        /// </summary>
        Task<Payment?> GetByTransactionRefAsync(string transactionRef, bool asNoTracking);

        // tạo payment mới, trả về payment đã được tạo 
        Task<Payment> CreateAsync(Payment payment);

        // cập nhật payment
        Task UpdateAsync(Payment payment);
        Task<decimal> GetCollectedAmountAsync(Guid invoiceId);

        // Ghi nhận tiền về: mọi trạng thái trừ SUCCESS (link cũ đã bị đánh FAILED khi thanh toán lại vẫn được ghi nhận); false khi đã ghi nhận
        Task<bool> TryMarkSuccessAsync(Guid paymentId, DateTime nowUtc);

        // Thanh toán thất bại: chỉ Payment còn PENDING; false với link cũ hoặc kết quả lặp
        Task<bool> TryFailPendingAsync(Guid paymentId, DateTime nowUtc);

        // Đánh FAILED mọi Payment còn PENDING của hóa đơn, trừ exceptPaymentId; trả số dòng đã đổi
        Task<int> FailPendingByInvoiceAsync(Guid invoiceId, Guid? exceptPaymentId, DateTime nowUtc);

        // Các Payment đã thu của hóa đơn (không track)
        Task<List<Payment>> GetSuccessfulByInvoiceAsync(Guid invoiceId);
        Task CreateIpnLogAsync(PaymentIpnLog log);
    }
}
