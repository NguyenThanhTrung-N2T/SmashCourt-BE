using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class SlotInterestRepository : ISlotInterestRepository
    {
        private readonly SmashCourtContext _context;

        public SlotInterestRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task CreateAsync(SlotInterest interest)
        {
            _context.SlotInterests.Add(interest);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Kiá»ƒm tra duplicate: cÃ¹ng email + court + date + slot trÃ¹ng giá» (overlap).
        /// DÃ¹ng overlap logic giá»‘ng SlotLock Ä‘á»ƒ báº¯t cÃ¡c trÆ°á»ng há»£p Ä‘áº·t nhiá»u khung giá» liÃªn tiáº¿p.
        /// </summary>
        public async Task<bool> ExistsAsync(
            Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string email)
        {
            return await _context.SlotInterests
                .AnyAsync(si =>
                    si.CourtId == courtId &&
                    si.Date == date &&
                    si.StartTime < endTime &&
                    si.EndTime > startTime &&
                    si.Email == email &&
                    si.ExpiresAt > DateTimeHelper.GetUtcNow());
        }

        /// <summary>
        /// Lấy tất cả interest records overlap với slot vừa được giải phóng.
        /// </summary>
        public async Task<List<SlotInterest>> GetOverlappingSlotInterestsAsync(
            Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
        {
            return await _context.SlotInterests
                .Where(si =>
                    si.CourtId == courtId &&
                    si.Date == date &&
                    si.StartTime < endTime &&
                    si.EndTime > startTime &&
                    si.ExpiresAt > DateTimeHelper.GetUtcNow())
                .ToListAsync();
        }

        /// <summary>
        /// Xóa tất cả interest records overlap với slot sau khi đã notify (one-shot).
        /// </summary>
        public async Task DeleteOverlappingSlotInterestsAsync(
            Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
        {
            await _context.SlotInterests
                .Where(si =>
                    si.CourtId == courtId &&
                    si.Date == date &&
                    si.StartTime < endTime &&
                    si.EndTime > startTime)
                .ExecuteDeleteAsync();
        }

        /// <summary>Cleanup job: xÃ³a records Ä‘Ã£ háº¿t háº¡n. Tráº£ vá» sá»‘ lÆ°á»£ng Ä‘Ã£ xÃ³a.</summary>
        public async Task<int> DeleteExpiredAsync()
        {
            return await _context.SlotInterests
                .Where(si => si.ExpiresAt <= DateTimeHelper.GetUtcNow())
                .ExecuteDeleteAsync();
        }
    }
}

