using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class LoyaltyTierRepository : ILoyaltyTierRepository
    {
        private readonly SmashCourtContext _db;

        public LoyaltyTierRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // Lấy danh sách tất cả các rank khách hàng, sắp xếp theo MinPoints tăng dần
        public async Task<IEnumerable<LoyaltyTier>> GetAllLoyaltyTiersAsync()
        {
            return await _db.LoyaltyTiers
                .OrderBy(t => t.MinPoints)
                .ToListAsync();
        }

        // Lấy thông tin chi tiết của một rank khách hàng theo Id
        public async Task<LoyaltyTier?> GetLoyaltyTierByIdAsync(Guid id)
        {
            return await _db.LoyaltyTiers.FindAsync(id);
        }

        // Lấy tất cả các hạng thành viên trừ hạng có Id được truyền vào, sắp xếp theo MinPoints tăng dần
        public async Task<IEnumerable<LoyaltyTier>> GetAllExceptAsync(Guid excludeId)
        {
            return await _db.LoyaltyTiers
                .Where(t => t.Id != excludeId)
                .OrderBy(t => t.MinPoints)
                .ToListAsync();
        }

        // Cập nhật thông tin của một rank khách hàng
        public async Task UpdateAsync(LoyaltyTier tier)
        {
            _db.LoyaltyTiers.Update(tier);
            await _db.SaveChangesAsync();
        }

        // Lấy hạng khách hàng tiếp theo dựa trên số điểm hiện tại của khách hàng
        public async Task<LoyaltyTier?> GetNextTierAsync(int currentMinPoints)
        {
            return await _db.LoyaltyTiers
                .Where(t => t.MinPoints > currentMinPoints)
                .OrderBy(t => t.MinPoints)
                .FirstOrDefaultAsync();
        }
    }
}
