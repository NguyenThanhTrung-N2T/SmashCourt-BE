using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface ITimeSlotService
    {
        // Lấy tất cả slot, group theo start + end để trả về 1 record duy nhất cho WEEKDAY + WEEKEND
        Task<List<TimeSlotDto>> GetAllAsync();

        // tạo thêm slot mới
        Task<TimeSlotDto> CreateAsync(CreateTimeSlotDto dto);

        // cập nhật slot, nếu start + end đã tồn tại thì sẽ update record đó
        Task<TimeSlotDto> UpdateAsync(Guid id, CreateTimeSlotDto dto);

        // xóa slot, nếu đang được dùng trong system_prices hoặc bookings thì sẽ lỗi
        Task DeleteAsync(Guid id);
    }
}
