using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ILoyaltyTierRepository
    {
        // lấy danh sách tất cả các rank khách hàng
        Task<IEnumerable<LoyaltyTier>> GetAllLoyaltyTiersAsync();

        // lấy thông tin chi tiết của một rank khách hàng theo id
        Task<LoyaltyTier?> GetLoyaltyTierByIdAsync(Guid id);

        // lấy tất cả các hạng thành viên trừ hạng có Id được truyền vào, sắp xếp theo MinPoints tăng dần
        Task<IEnumerable<LoyaltyTier>> GetAllExceptAsync(Guid excludeId);

        // cập nhật thông tin của một rank khách hàng
        Task UpdateAsync(LoyaltyTier tier);

        // lấy hạng tiếp theo dựa trên số điểm tối thiểu hiện tại
        Task<LoyaltyTier?> GetNextTierAsync(int currentMinPoints);

        // lấy hạng mặc định cho người dùng mới
        Task<LoyaltyTier?> GetDefaultTierAsync();
    }
}
