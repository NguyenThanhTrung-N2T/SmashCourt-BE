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
        private static readonly TimeZoneInfo VnTimezone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

        public PromotionRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // OWNER/MANAGER/STAFF — thấy tất cả trừ DELETED
        public async Task<PagedResult<Promotion>> GetAllAsync(int page, int pageSize)
        {
            var query = _context.Promotions
                .Where(p => p.Status != PromotionStatus.DELETED)
                .OrderByDescending(p => p.DiscountRate);

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
                .Where(p => p.Status == PromotionStatus.ACTIVE)
                .OrderByDescending(p => p.DiscountRate)
                .ToListAsync();
        }

        public async Task<Promotion?> GetByIdAsync(Guid id)
        {
            return await _context.Promotions
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.Status != PromotionStatus.DELETED);
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

        // Scheduled job — cập nhật status theo ngày
        public async Task UpdateExpiredStatusAsync()
        {
            var vnNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, VnTimezone);
            var today = DateOnly.FromDateTime(vnNow);

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
    }
}
