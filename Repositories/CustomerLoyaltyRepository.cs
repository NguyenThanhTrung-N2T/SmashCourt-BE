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

        public async Task<CustomerLoyalty?> GetByUserIdAsync(Guid userId)
        {
            return await _db.CustomerLoyalties
                .Include(cl => cl.Tier)
                .FirstOrDefaultAsync(cl => cl.UserId == userId);
        }
    }
}
