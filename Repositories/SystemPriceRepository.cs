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

        // Giá đang áp dụng — lấy effective_from mới nhất <= today, xử lý hoàn toàn trên DB
        public async Task<List<SystemPrice>> GetCurrentAsync(Guid? courtTypeId = null)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            // Subquery: max effective_from cho mỗi (CourtTypeId, TimeSlotId)
            var latestDates = _context.SystemPrices
                .Where(sp =>
                    sp.EffectiveFrom <= today &&
                    (courtTypeId == null || sp.CourtTypeId == courtTypeId))
                .GroupBy(sp => new { sp.CourtTypeId, sp.TimeSlotId })
                .Select(g => new
                {
                    g.Key.CourtTypeId,
                    g.Key.TimeSlotId,
                    MaxDate = g.Max(sp => sp.EffectiveFrom)
                });

            // JOIN với subquery — toàn bộ xử lý trên DB
            return await _context.SystemPrices
                .Include(sp => sp.CourtType)
                .Include(sp => sp.TimeSlot)
                .Join(
                    latestDates,
                    sp => new { sp.CourtTypeId, sp.TimeSlotId, sp.EffectiveFrom },
                    ld => new { ld.CourtTypeId, ld.TimeSlotId, EffectiveFrom = ld.MaxDate },
                    (sp, _) => sp)
                .OrderBy(sp => sp.CourtType.Name)
                .ThenBy(sp => sp.TimeSlot.StartTime)
                .ThenBy(sp => sp.TimeSlot.DayType)
                .ToListAsync();
        }

        // Giá tại một thời điểm cụ thể — lấy effective_from mới nhất <= targetDate, xử lý hoàn toàn trên DB
        public async Task<List<SystemPrice>> GetCurrentForDateAsync(DateOnly targetDate, Guid? courtTypeId = null)
        {
            // Subquery: max effective_from cho mỗi (CourtTypeId, TimeSlotId)
            var latestDates = _context.SystemPrices
                .Where(sp =>
                    sp.EffectiveFrom <= targetDate &&
                    (courtTypeId == null || sp.CourtTypeId == courtTypeId))
                .GroupBy(sp => new { sp.CourtTypeId, sp.TimeSlotId })
                .Select(g => new
                {
                    g.Key.CourtTypeId,
                    g.Key.TimeSlotId,
                    MaxDate = g.Max(sp => sp.EffectiveFrom)
                });

            // JOIN với subquery — toàn bộ xử lý trên DB
            return await _context.SystemPrices
                .Include(sp => sp.CourtType)
                .Include(sp => sp.TimeSlot)
                .Join(
                    latestDates,
                    sp => new { sp.CourtTypeId, sp.TimeSlotId, sp.EffectiveFrom },
                    ld => new { ld.CourtTypeId, ld.TimeSlotId, EffectiveFrom = ld.MaxDate },
                    (sp, _) => sp)
                .OrderBy(sp => sp.CourtType.Name)
                .ThenBy(sp => sp.TimeSlot.StartTime)
                .ThenBy(sp => sp.TimeSlot.DayType)
                .ToListAsync();
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

        // Lấy giá chung hiện tại cho một court type cụ thể (dùng trong booking để tính giá), xử lý hoàn toàn trên DB
        public async Task<List<SystemPrice>> GetCurrentRawAsync(Guid courtTypeId)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            // Subquery: max effective_from cho mỗi TimeSlotId
            var latestDates = _context.SystemPrices
                .Where(sp =>
                    sp.CourtTypeId == courtTypeId &&
                    sp.EffectiveFrom <= today)
                .GroupBy(sp => sp.TimeSlotId)
                .Select(g => new
                {
                    TimeSlotId = g.Key,
                    MaxDate = g.Max(sp => sp.EffectiveFrom)
                });

            // JOIN với subquery — toàn bộ xử lý trên DB
            return await _context.SystemPrices
                .Include(sp => sp.TimeSlot)
                .Where(sp => sp.CourtTypeId == courtTypeId)
                .Join(
                    latestDates,
                    sp => new { sp.TimeSlotId, sp.EffectiveFrom },
                    ld => new { ld.TimeSlotId, EffectiveFrom = ld.MaxDate },
                    (sp, _) => sp)
                .ToListAsync();
        }

        // Lấy danh sách các ngày hiệu lực của giá chung cho một loại sân cụ thể
        public async Task<List<DateOnly>> GetVersionsAsync(Guid courtTypeId)
        {
            return await _context.SystemPrices
                .Where(sp => sp.CourtTypeId == courtTypeId)
                .Select(sp => sp.EffectiveFrom)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();
        }

        // Lấy các giá chung của 1 loại sân tại 1 ngày hiệu lực chính xác
        public async Task<List<SystemPrice>> GetExactDatePricesAsync(Guid courtTypeId, DateOnly effectiveFrom)
        {
            return await _context.SystemPrices
                .Where(sp => sp.CourtTypeId == courtTypeId && sp.EffectiveFrom == effectiveFrom)
                .ToListAsync();
        }

        // Upsert batch trong 1 transaction
        public async Task UpsertBatchAsync(List<SystemPrice> insertPrices, List<SystemPrice> updatePrices)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (insertPrices.Any())
                {
                    _context.SystemPrices.AddRange(insertPrices);
                }
                
                if (updatePrices.Any())
                {
                    _context.SystemPrices.UpdateRange(updatePrices);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

    }
}
