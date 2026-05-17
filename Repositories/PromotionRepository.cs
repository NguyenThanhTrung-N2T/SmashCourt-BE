using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class PromotionRepository : IPromotionRepository
    {
        private readonly SmashCourtContext _context;

        public PromotionRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // OWNER/MANAGER/STAFF — thấy tất cả trừ DELETED
        public async Task<PagedResult<Promotion>> GetAllAsync(int page, int pageSize)
        {
            var query = _context.Promotions
                .Include(p => p.Conditions)
                .Where(p => p.Status != PromotionStatus.DELETED)
                .OrderByDescending(p => p.CreatedAt);

            var totalItems = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Promotion>
            {
                Items = items,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }

        // Chỉ lấy ACTIVE — dùng khi đặt sân
        public async Task<List<Promotion>> GetActiveAsync()
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .Where(p => p.Status == PromotionStatus.ACTIVE)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Promotion>> GetActiveWithConditionsAsync()
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .Where(p => p.Status == PromotionStatus.ACTIVE)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Promotion>> GetApplicableByDateAsync(DateOnly usageDate)
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .Where(p => p.Status != PromotionStatus.DELETED &&
                            p.StartDate <= usageDate &&
                            p.EndDate >= usageDate)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<Promotion?> GetByIdAsync(Guid id)
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.Status != PromotionStatus.DELETED);
        }

        public async Task<Promotion?> GetByIdWithConditionsAsync(Guid id)
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task<bool> CodeExistsAsync(string code, Guid? excludeId = null)
        {
            var query = _context.Promotions.Where(p => p.Code == code);

            if (excludeId.HasValue)
            {
                query = query.Where(p => p.Id != excludeId.Value);
            }

            return await query.AnyAsync();
        }

        public async Task<Promotion> CreateAsync(Promotion promotion)
        {
            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();
            return promotion;
        }

        public async Task UpdateAsync(Promotion promotion)
        {
            _context.Promotions.Update(promotion);
            await _context.SaveChangesAsync();
        }

        public async Task RemoveConditionsAsync(Guid promotionId)
        {
            var conditions = await _context.PromotionConditions
                .Where(c => c.PromotionId == promotionId)
                .ToListAsync();

            _context.PromotionConditions.RemoveRange(conditions);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateExpiredStatusAsync()
        {
            var today = SmashCourt_BE.Helpers.DateTimeHelper.GetTodayInVietnam();

            // INACTIVE → ACTIVE nếu đến ngày bắt đầu
            await _context.Promotions
                .Where(p =>
                    p.Status == PromotionStatus.INACTIVE &&
                    p.StartDate <= today &&
                    p.EndDate >= today)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PromotionStatus.ACTIVE)
                    .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

            // ACTIVE → INACTIVE nếu qua ngày kết thúc
            await _context.Promotions
                .Where(p =>
                    p.Status == PromotionStatus.ACTIVE &&
                    p.EndDate < today)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PromotionStatus.INACTIVE)
                    .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
        }

        /// <summary>
        /// Lấy promotion theo code kèm conditions (không lấy promotion đã xóa) — dùng trong PromotionEngineService.
        /// </summary>
        public async Task<Promotion?> GetByCodeNotDeletedAsync(string code)
        {
            return await _context.Promotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p => p.Code == code && p.Status != PromotionStatus.DELETED);
        }

        /// <summary>
        /// Đếm số lần user đã sử dụng một promotion — dùng kiểm tra UsagePerUserLimit.
        /// </summary>
        public async Task<int> GetUserUsageCountAsync(Guid promotionId, Guid userId)
        {
            return await _context.BookingPromotions
                .Where(bp => bp.PromotionId == promotionId)
                .Join(_context.Bookings,
                    bp => bp.BookingId,
                    b => b.Id,
                    (bp, b) => new { bp, b })
                .Where(x => x.b.CustomerId == userId)
                .CountAsync();
        }

        /// <summary>
        /// Tăng UsedCount bằng atomic UPDATE — tránh race condition khi nhiều request đồng thời.
        /// </summary>
        public async Task IncrementUsageCountAsync(Guid promotionId)
        {
            await _context.Promotions
                .Where(p => p.Id == promotionId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount + 1));
        }

        /// <summary>
        /// Giảm UsedCount khi booking bị hủy — giải phóng slot promotion cho customer khác.
        /// Sử dụng atomic UPDATE để tránh UsedCount âm.
        /// </summary>
        public async Task DecrementUsageCountAsync(Guid promotionId)
        {
            await _context.Promotions
                .Where(p => p.Id == promotionId && p.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UsedCount, p => p.UsedCount - 1));
        }
    }
}
