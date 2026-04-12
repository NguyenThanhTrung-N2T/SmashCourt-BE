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
    }
}
