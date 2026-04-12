using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISlotLockRepository
    {
        Task<SlotLock?> GetByCourtAndTimeAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);
        Task<SlotLock> CreateAsync(SlotLock slotLock);
        Task DeleteAsync(Guid id);
        Task DeleteExpiredAsync(Guid courtId);
        Task DeleteByBookingIdAsync(Guid bookingId);
    }
}
