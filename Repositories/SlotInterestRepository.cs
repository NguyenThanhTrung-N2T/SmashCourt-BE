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
        /// Kiểm tra duplicate: cùng email + court + date + slot trùng giờ (overlap).
        /// Dùng overlap logic giống SlotLock để bắt các trường hợp đặt nhiều khung giờ liên tiếp.
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
        /// Trả về số rows đã xóa.
        /// </summary>
        public async Task<int> DeleteOverlappingSlotInterestsAsync(
            Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime)
        {
            return await _context.SlotInterests
                .Where(si =>
                    si.CourtId == courtId &&
                    si.Date == date &&
                    si.StartTime < endTime &&
                    si.EndTime > startTime)
                .ExecuteDeleteAsync();
        }

        /// <summary>Cleanup job: xóa records đã hết hạn. Trả về số lượng đã xóa.</summary>
        public async Task<int> DeleteExpiredAsync()
        {
            return await _context.SlotInterests
                .Where(si => si.ExpiresAt <= DateTimeHelper.GetUtcNow())
                .ExecuteDeleteAsync();
        }
    }
}

