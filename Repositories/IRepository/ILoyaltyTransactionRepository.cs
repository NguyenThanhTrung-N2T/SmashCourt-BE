using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ILoyaltyTransactionRepository
    {
        // lấy danh sách giao dịch của người dùng với phân trang
        Task<(IEnumerable<LoyaltyTransaction> Items, int Total)> GetByUserIdAsync(
        Guid userId, int page, int pageSize);

        // ghi lịch sử giao dịch điểm mới (earn / redeem)
        Task AddAsync(LoyaltyTransaction transaction);

        // lấy transaction theo booking ID (để check đã cộng điểm chưa)
        Task<LoyaltyTransaction?> GetByBookingIdAsync(Guid bookingId);

        // lấy transaction DEDUCT theo booking ID (để check đã trừ điểm chưa)
        Task<LoyaltyTransaction?> GetDeductByBookingIdAsync(Guid bookingId);
    }
}
