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
        Task CreateIpnLogAsync(PaymentIpnLog log);
    }
}
