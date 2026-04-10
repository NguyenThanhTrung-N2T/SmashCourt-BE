using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Repositories
{
    public class SystemPriceRepository : ISystemPriceRepository
    {
        private readonly SmashCourtContext _context;

        public SystemPriceRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lịch sử toàn bộ giá — filter theo court type nếu có
        public async Task<List<SystemPrice>> GetAllAsync(Guid? courtTypeId = null)
        {
            return await _context.SystemPrices
                .Include(sp => sp.CourtType)
                .Include(sp => sp.TimeSlot)
                .Where(sp => courtTypeId == null || sp.CourtTypeId == courtTypeId)
                .OrderBy(sp => sp.CourtTypeId)
                .ThenBy(sp => sp.TimeSlot.StartTime)
                .ThenBy(sp => sp.TimeSlot.DayType)
                .ThenByDescending(sp => sp.EffectiveFrom)
                .ToListAsync();
        }

        // Giá đang áp dụng — lấy effective_from mới nhất <= today
        public async Task<List<SystemPrice>> GetCurrentAsync(Guid? courtTypeId = null)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            // Dùng raw SQL để DISTINCT ON hiệu quả hơn
            // EF Core: GroupBy + lấy first theo effectiveFrom DESC
            var query = _context.SystemPrices
                .Include(sp => sp.CourtType)
                .Include(sp => sp.TimeSlot)
                .Where(sp =>
                    sp.EffectiveFrom <= today &&
                    (courtTypeId == null || sp.CourtTypeId == courtTypeId));

            // Group theo courtTypeId + timeSlotId → lấy effective_from mới nhất
            var grouped = await query
                .GroupBy(sp => new { sp.CourtTypeId, sp.TimeSlotId })
                .Select(g => g.OrderByDescending(sp => sp.EffectiveFrom).First())
                .ToListAsync();

            return grouped
                .OrderBy(sp => sp.CourtType.Name)
                .ThenBy(sp => sp.TimeSlot.StartTime)
                .ThenBy(sp => sp.TimeSlot.DayType)
                .ToList();
        }

        // Giá tại một thời điểm cụ thể — lấy effective_from mới nhất <= targetDate
        public async Task<List<SystemPrice>> GetCurrentForDateAsync(DateOnly targetDate, Guid? courtTypeId = null)
        {
            var query = _context.SystemPrices
                .Include(sp => sp.CourtType)
                .Include(sp => sp.TimeSlot)
                .Where(sp =>
                    sp.EffectiveFrom <= targetDate &&
                    (courtTypeId == null || sp.CourtTypeId == courtTypeId));

            var grouped = await query
                .GroupBy(sp => new { sp.CourtTypeId, sp.TimeSlotId })
                .Select(g => g.OrderByDescending(sp => sp.EffectiveFrom).First())
                .ToListAsync();

            return grouped
                .OrderBy(sp => sp.CourtType.Name)
                .ThenBy(sp => sp.TimeSlot.StartTime)
                .ThenBy(sp => sp.TimeSlot.DayType)
                .ToList();
        }

        // Kiểm tra tồn tại của một record với courtTypeId + timeSlotId + effectiveFrom
        public async Task<bool> ExistsAsync(
            Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom)
        {
            return await _context.SystemPrices
                .AnyAsync(sp =>
                    sp.CourtTypeId == courtTypeId &&
                    sp.TimeSlotId == timeSlotId &&
                    sp.EffectiveFrom == effectiveFrom);
        }

        // Insert batch trong 1 transaction
        public async Task CreateBatchAsync(List<SystemPrice> prices)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.SystemPrices.AddRange(prices);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Lấy giá chung hiện tại cho một court type cụ thể (dùng trong booking để tính giá)
        public async Task<List<SystemPrice>> GetCurrentRawAsync(Guid courtTypeId)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            var raw = await _context.SystemPrices
                .Include(sp => sp.TimeSlot)
                .Where(sp =>
                    sp.CourtTypeId == courtTypeId &&
                    sp.EffectiveFrom <= today)
                .ToListAsync();

            return raw
                .GroupBy(sp => sp.TimeSlotId)
                .Select(g => g.OrderByDescending(sp => sp.EffectiveFrom).First())
                .ToList();
        }

    }
}
