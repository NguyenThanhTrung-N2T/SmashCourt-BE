using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ILoyaltyTransactionRepository
    {
        // lấy danh sách giao dịch của người dùng với phân trang
        Task<(IEnumerable<LoyaltyTransaction> Items, int Total)> GetByUserIdAsync(
        Guid userId, int page, int pageSize);
    }
}
