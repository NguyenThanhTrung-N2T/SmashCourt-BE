using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISlotLockRepository
    {
        // Lấy slot lock của sân vào một khoảng thời gian cụ thể, nếu có
        Task<SlotLock?> GetByCourtAndTimeAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        // Lấy tất cả slot lock của sân vào một ngày cụ thể
        Task<List<SlotLock>> GetByCourtAndDateAsync(Guid courtId, DateOnly date);

        // tạo một slot lock mới
        Task<SlotLock> CreateAsync(SlotLock slotLock);


        Task DeleteAsync(Guid id);

        // Xóa tất cả slot lock đã hết hạn của chi nhánh
        Task DeleteExpiredByBranchAsync(Guid branchId);

        // Xóa tất cả slot lock liên quan đến một booking cụ thể
        Task DeleteByBookingIdAsync(Guid bookingId);
    }
}
