using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Repositories
{
    public class BranchPriceRepository : IBranchPriceRepository
    {
        private readonly SmashCourtContext _context;

        public BranchPriceRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lấy tất cả giá override của chi nhánh, có thể lọc theo loại sân
        public async Task<List<BranchPriceOverride>> GetAllAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            return await _context.BranchPriceOverrides
                .Include(bp => bp.CourtType)
                .Include(bp => bp.TimeSlot)
                .Where(bp =>
                    bp.BranchId == branchId &&
                    (courtTypeId == null || bp.CourtTypeId == courtTypeId))
                .OrderBy(bp => bp.CourtType.Name)
                .ThenBy(bp => bp.TimeSlot.StartTime)
                .ThenBy(bp => bp.TimeSlot.DayType)
                .ThenByDescending(bp => bp.EffectiveFrom)
                .ToListAsync();
        }

        // Lấy giá override hiện tại của chi nhánh, có thể lọc theo loại sân
        public async Task<List<BranchPriceOverride>> GetCurrentAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            var raw = await _context.BranchPriceOverrides
                .Include(bp => bp.CourtType)
                .Include(bp => bp.TimeSlot)
                .Where(bp =>
                    bp.BranchId == branchId &&
                    bp.EffectiveFrom <= today &&
                    (courtTypeId == null || bp.CourtTypeId == courtTypeId))
                .ToListAsync();

            return raw
                .GroupBy(bp => new { bp.CourtTypeId, bp.TimeSlotId })
                .Select(g => g.OrderByDescending(bp => bp.EffectiveFrom).First())
                .ToList();
        }

        // Lấy giá override của chi nhánh có hiệu lực tại một ngày cụ thể, có thể lọc theo loại sân
        public async Task<List<BranchPriceOverride>> GetCurrentForDateAsync(
            Guid branchId, DateOnly targetDate, Guid? courtTypeId = null)
        {
            var raw = await _context.BranchPriceOverrides
                .Include(bp => bp.CourtType)
                .Include(bp => bp.TimeSlot)
                .Where(bp =>
                    bp.BranchId == branchId &&
                    bp.EffectiveFrom <= targetDate &&
                    (courtTypeId == null || bp.CourtTypeId == courtTypeId))
                .ToListAsync();

            return raw
                .GroupBy(bp => new { bp.CourtTypeId, bp.TimeSlotId })
                .Select(g => g.OrderByDescending(bp => bp.EffectiveFrom).First())
                .ToList();
        }


        // Kiểm tra đã tồn tại giá override nào cho branch + court type + time slot + effective_from cụ thể chưa
        public async Task<bool> ExistsAsync(
            Guid branchId, Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom)
        {
            return await _context.BranchPriceOverrides
                .AnyAsync(bp =>
                    bp.BranchId == branchId &&
                    bp.CourtTypeId == courtTypeId &&
                    bp.TimeSlotId == timeSlotId &&
                    bp.EffectiveFrom == effectiveFrom);
        }

        // Tạo batch giá override mới cho 1 branch + court type với ngày hiệu lực cụ thể. Cả cặp WEEKDAY + WEEKEND phải được tạo cùng lúc.
        public async Task CreateBatchAsync(List<BranchPriceOverride> prices)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.BranchPriceOverrides.AddRange(prices);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Xóa cặp giá override (WEEKDAY + WEEKEND) của 1 branch + court type với ngày hiệu lực và khung giờ cụ thể
        public async Task<int> DeletePairAsync(
            Guid branchId, Guid courtTypeId, DateOnly effectiveFrom,
            TimeOnly startTime, TimeOnly endTime)
        {
            // Load Id của cả 2 bản ghi (WEEKDAY + WEEKEND) cùng khung giờ
            var ids = await _context.BranchPriceOverrides
                .Where(bp =>
                    bp.BranchId == branchId &&
                    bp.CourtTypeId == courtTypeId &&
                    bp.EffectiveFrom == effectiveFrom &&
                    bp.TimeSlot.StartTime == startTime &&
                    bp.TimeSlot.EndTime == endTime)
                .Select(bp => bp.Id)
                .ToListAsync();

            return await _context.BranchPriceOverrides
                .Where(bp => ids.Contains(bp.Id))
                .ExecuteDeleteAsync();
        }

        // Lấy các giá override của 1 chi nhánh + 1 loại sân tại 1 ngày hiệu lực chính xác
        public async Task<List<BranchPriceOverride>> GetExactDatePricesAsync(Guid branchId, Guid courtTypeId, DateOnly effectiveFrom)
        {
            return await _context.BranchPriceOverrides
                .Where(bp => bp.BranchId == branchId && bp.CourtTypeId == courtTypeId && bp.EffectiveFrom == effectiveFrom)
                .ToListAsync();
        }

        // Upsert batch (Cập nhật nếu đã có, tạo mới nếu chưa)
        public async Task UpsertBatchAsync(List<BranchPriceOverride> insertPrices, List<BranchPriceOverride> updatePrices)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (insertPrices.Any())
                {
                    _context.BranchPriceOverrides.AddRange(insertPrices);
                }
                
                if (updatePrices.Any())
                {
                    _context.BranchPriceOverrides.UpdateRange(updatePrices);
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
