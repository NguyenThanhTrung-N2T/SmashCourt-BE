using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
namespace SmashCourt_BE.Repositories
{
    public class CustomerLoyaltyRepository : ICustomerLoyaltyRepository
    {
        private readonly SmashCourtContext _db;

        public CustomerLoyaltyRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // Lấy thông tin loyalty của khách hàng theo userId
        public async Task<CustomerLoyalty?> GetByUserIdAsync(Guid userId)
        {
            return await _db.CustomerLoyalties
                .Include(cl => cl.Tier)
                .FirstOrDefaultAsync(cl => cl.UserId == userId);
        }

        // Tạo mới loyalty cho khách hàng
        public async Task CreateAsync(CustomerLoyalty customerLoyalty)
        {
            await _db.CustomerLoyalties.AddAsync(customerLoyalty);
            await _db.SaveChangesAsync();
        }

        // Cập nhật thông tin loyalty của khách hàng
        public async Task UpdateAsync(CustomerLoyalty loyalty)
        {
            _db.CustomerLoyalties.Update(loyalty);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Cộng/trừ điểm loyalty một cách atomic để tránh race condition
        /// Sử dụng UPDATE SET total_points = total_points + @points thay vì read-modify-write
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <param name="points">Số điểm cần cộng (dương) hoặc trừ (âm)</param>
        /// <returns>Tổng điểm mới sau khi cập nhật</returns>
        public async Task<int> AddPointsAtomicAsync(Guid userId, int points)
        {
            // Atomic update: UPDATE customer_loyalties SET total_points = total_points + @points
            // Không cho phép total_points < 0
            await _db.CustomerLoyalties
                .Where(cl => cl.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(cl => cl.TotalPoints, 
                        cl => cl.TotalPoints + points >= 0 
                            ? cl.TotalPoints + points 
                            : 0)
                    .SetProperty(cl => cl.UpdatedAt, DateTime.UtcNow));

            // Đọc lại giá trị mới để return
            var loyalty = await _db.CustomerLoyalties
                .Where(cl => cl.UserId == userId)
                .Select(cl => cl.TotalPoints)
                .FirstOrDefaultAsync();

            return loyalty;
        }

        /// <summary>
        /// Cập nhật tier của user một cách atomic
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <param name="tierId">ID của tier mới</param>
        public async Task UpdateTierAsync(Guid userId, Guid tierId)
        {
            await _db.CustomerLoyalties
                .Where(cl => cl.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(cl => cl.TierId, tierId)
                    .SetProperty(cl => cl.UpdatedAt, DateTime.UtcNow));
        }
    }
}
