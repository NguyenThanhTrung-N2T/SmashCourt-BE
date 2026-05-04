using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
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

        // ghi giao dịch điểm mới
        public async Task AddAsync(LoyaltyTransaction transaction)
        {
            await _db.LoyaltyTransactions.AddAsync(transaction);
            await _db.SaveChangesAsync();
        }

        /// <summary>
        /// Lấy transaction EARN (cộng điểm) theo booking ID
        /// Dùng để kiểm tra booking đã được cộng điểm chưa trước khi trừ điểm
        /// </summary>
        /// <param name="bookingId">Booking ID</param>
        /// <returns>Transaction EARN gần nhất, hoặc null nếu chưa cộng điểm</returns>
        public async Task<LoyaltyTransaction?> GetByBookingIdAsync(Guid bookingId)
        {
            return await _db.LoyaltyTransactions
                .Where(t => t.BookingId == bookingId && t.Type == LoyaltyTransactionType.EARN)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Lấy transaction DEDUCT (trừ điểm) theo booking ID
        /// Dùng để kiểm tra booking đã bị trừ điểm chưa (tránh trừ lặp)
        /// </summary>
        /// <param name="bookingId">Booking ID</param>
        /// <returns>Transaction DEDUCT gần nhất, hoặc null nếu chưa trừ điểm</returns>
        public async Task<LoyaltyTransaction?> GetDeductByBookingIdAsync(Guid bookingId)
        {
            return await _db.LoyaltyTransactions
                .Where(t => t.BookingId == bookingId && t.Type == LoyaltyTransactionType.DEDUCT)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();
        }
    }
}
