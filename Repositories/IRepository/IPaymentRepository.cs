using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IPaymentRepository
    {
        Task<Payment?> GetByTransactionRefAsync(string transactionRef);
        Task<Payment> CreateAsync(Payment payment);
        Task UpdateAsync(Payment payment);
        Task CreateIpnLogAsync(PaymentIpnLog log);
    }
}
