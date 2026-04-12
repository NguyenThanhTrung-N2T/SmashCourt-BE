using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface ITimeGridService
    {
        Task<List<TimeGridSlotDto>> GetTimeGridAsync(
            Guid branchId, Guid courtId, DateOnly date);

    }
}
