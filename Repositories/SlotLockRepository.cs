using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class SlotLockRepository : ISlotLockRepository
    {
        private readonly SmashCourtContext _context;

        public SlotLockRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<SlotLock?> GetByCourtAndTimeAsync(
            Guid courtId, DateOnly date,
            TimeOnly startTime, TimeOnly endTime)
        {
            return await _context.SlotLocks
                .FirstOrDefaultAsync(sl =>
                    sl.CourtId == courtId &&
                    sl.Date == date &&
                    sl.StartTime < endTime &&
                    sl.EndTime > startTime &&
                    sl.ExpiresAt > DateTime.UtcNow);
        }

        public async Task<SlotLock> CreateAsync(SlotLock slotLock)
        {
            _context.SlotLocks.Add(slotLock);
            await _context.SaveChangesAsync();
            return slotLock;
        }

        public async Task DeleteAsync(Guid id)
        {
            await _context.SlotLocks
                .Where(sl => sl.Id == id)
                .ExecuteDeleteAsync();
        }

        // Xóa expired locks của court trước khi INSERT mới
        public async Task DeleteExpiredAsync(Guid courtId)
        {
            await _context.SlotLocks
                .Where(sl =>
                    sl.CourtId == courtId &&
                    sl.ExpiresAt <= DateTime.UtcNow)
                .ExecuteDeleteAsync();
        }

        // Batch load tất cả lock của court trong ngày — dùng cho TimeGrid
        public async Task<List<SlotLock>> GetByCourtAndDateAsync(
            Guid courtId, DateOnly date)
        {
            return await _context.SlotLocks
                .Where(sl =>
                    sl.CourtId == courtId &&
                    sl.Date == date &&
                    sl.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();
        }

        public async Task DeleteByBookingIdAsync(Guid bookingId)
        {
            await _context.SlotLocks
                .Where(sl => sl.BookingId == bookingId)
                .ExecuteDeleteAsync();
        }
    }
}
