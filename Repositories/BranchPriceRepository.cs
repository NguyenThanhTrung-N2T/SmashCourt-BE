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

        public async Task<List<BranchPriceOverride>> GetCurrentAsync(
            Guid branchId, Guid? courtTypeId = null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var query = _context.BranchPriceOverrides
                .Include(bp => bp.CourtType)
                .Include(bp => bp.TimeSlot)
                .Where(bp =>
                    bp.BranchId == branchId &&
                    bp.EffectiveFrom <= today &&
                    (courtTypeId == null || bp.CourtTypeId == courtTypeId));

            return await query
                .GroupBy(bp => new { bp.CourtTypeId, bp.TimeSlotId })
                .Select(g => g.OrderByDescending(bp => bp.EffectiveFrom).First())
                .ToListAsync();
        }

        public async Task<BranchPriceOverride?> GetByIdAsync(Guid id)
        {
            return await _context.BranchPriceOverrides
                .Include(bp => bp.CourtType)
                .Include(bp => bp.TimeSlot)
                .FirstOrDefaultAsync(bp => bp.Id == id);
        }

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

        public async Task DeleteAsync(Guid id)
        {
            await _context.BranchPriceOverrides
                .Where(bp => bp.Id == id)
                .ExecuteDeleteAsync();
        }

        public async Task<List<BranchPriceOverride>> GetCurrentRawAsync(
    Guid branchId, Guid courtTypeId)
        {
            var today = DateTimeHelper.GetTodayInVietnam();

            return await _context.BranchPriceOverrides
                .Include(bp => bp.TimeSlot)
                .Where(bp =>
                    bp.BranchId == branchId &&
                    bp.CourtTypeId == courtTypeId &&
                    bp.EffectiveFrom <= today)
                .GroupBy(bp => new { bp.TimeSlotId })
                .Select(g => g.OrderByDescending(bp => bp.EffectiveFrom).First())
                .ToListAsync();
        }
    }
}
