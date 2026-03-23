using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class LoyaltyTransactionRepository : ILoyaltyTransactionRepository
    {
        private readonly SmashCourtContext _db;

        public LoyaltyTransactionRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // lấy lịch sử giao dịch điểm thưởng của người dùng, có phân trang
        public async Task<(IEnumerable<LoyaltyTransaction> Items, int Total)>
            GetByUserIdAsync(Guid userId, int page, int pageSize)
        {
            var query = _db.LoyaltyTransactions
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt);

            var total = await query.CountAsync();

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, total);
        }
    }
}
