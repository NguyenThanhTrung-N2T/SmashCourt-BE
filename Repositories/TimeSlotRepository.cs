using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class TimeSlotRepository : ITimeSlotRepository
    {
        private readonly SmashCourtContext _context;

        public TimeSlotRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lấy tất cả slot, sắp xếp theo startTime + dayType để dễ hiển thị
        public async Task<List<TimeSlot>> GetAllAsync()
        {
            return await _context.TimeSlots
                .OrderBy(ts => ts.StartTime)
                .ThenBy(ts => ts.DayType)
                .ToListAsync();
        }

        public async Task<TimeSlot?> GetByIdAsync(Guid id)
        {
            return await _context.TimeSlots.FindAsync(id);
        }

        public async Task<List<TimeSlot>> GetByTimeRangeAsync(
            TimeOnly startTime, TimeOnly endTime)
        {
            return await _context.TimeSlots
                .Where(ts =>
                    ts.StartTime == startTime &&
                    ts.EndTime == endTime)
                .ToListAsync();
        }

        public async Task<List<TimeSlot>> GetByDayTypeAsync(DayType dayType)
        {
            return await _context.TimeSlots
                .Where(ts => ts.DayType == dayType)
                .OrderBy(ts => ts.StartTime)
                .ToListAsync();
        }

        // Check overlap với slot khác — bỏ qua chính nó khi update
        public async Task<bool> HasOverlapAsync(
            TimeOnly startTime, TimeOnly endTime, Guid? excludeId = null)
        {
            // Lấy tất cả WEEKDAY slots (WEEKEND cùng range nên chỉ cần check 1 loại)
            var slots = await _context.TimeSlots
                .Where(ts =>
                    ts.DayType == DayType.WEEKDAY &&
                    (excludeId == null || ts.Id != excludeId))
                .ToListAsync();

            return slots.Any(ts =>
                startTime < ts.EndTime && endTime > ts.StartTime);
        }

        // Check slot đang được dùng trong system_prices hoặc bookings
        public async Task<bool> IsInUseAsync(Guid id)
        {
            return await _context.SystemPrices
                       .AnyAsync(sp => sp.TimeSlotId == id) ||
                   await _context.BranchPriceOverrides
                       .AnyAsync(bp => bp.TimeSlotId == id) ||
                   await _context.BookingPriceItems
                       .AnyAsync(bpi => bpi.TimeSlotId == id);
        }

        // Tạo cả WEEKDAY + WEEKEND trong 1 transaction
        public async Task<List<TimeSlot>> CreateBothAsync(
            TimeSlot weekday, TimeSlot weekend)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.TimeSlots.AddRange(weekday, weekend);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return [weekday, weekend];
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Update cả WEEKDAY + WEEKEND trong 1 transaction
        public async Task UpdateBothAsync(TimeSlot weekday, TimeSlot weekend)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.TimeSlots.UpdateRange(weekday, weekend);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Xóa cả WEEKDAY + WEEKEND theo startTime + endTime
        public async Task DeleteBothAsync(TimeOnly startTime, TimeOnly endTime)
        {
            await _context.TimeSlots
                .Where(ts =>
                    ts.StartTime == startTime &&
                    ts.EndTime == endTime)
                .ExecuteDeleteAsync();
        }
    }
}
