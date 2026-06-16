using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface ITimeGridService
    {
        // Lấy danh sách các khung giờ của một sân cụ thể trong một chi nhánh vào một ngày cụ thể
        Task<List<TimeGridSlotDto>> GetTimeGridAsync(
            Guid branchId, Guid courtId, DateOnly date);

    }
}
