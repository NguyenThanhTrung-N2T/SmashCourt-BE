using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ITimeSlotRepository
    {
        // lấy tất cả time slot
        Task<List<TimeSlot>> GetAllAsync();

        // lấy time slot theo id
        Task<TimeSlot?> GetByIdAsync(Guid id);

        // lấy time slot theo khoảng thời gian
        Task<List<TimeSlot>> GetByTimeRangeAsync(TimeOnly startTime, TimeOnly endTime);

        // kiểm tra overlap với slot khác — bỏ qua chính nó khi update
        Task<bool> HasOverlapAsync(TimeOnly startTime, TimeOnly endTime, Guid? excludeId = null);

        // kiểm tra slot đang được dùng trong system_prices hoặc bookings
        Task<bool> IsInUseAsync(Guid id);

        // tạo cả WEEKDAY + WEEKEND trong 1 transaction
        Task<List<TimeSlot>> CreateBothAsync(TimeSlot weekday, TimeSlot weekend);

        // cập nhật cả WEEKDAY + WEEKEND trong 1 transaction
        Task UpdateBothAsync(TimeSlot weekday, TimeSlot weekend);

        // xóa cả WEEKDAY + WEEKEND trong 1 transaction
        Task DeleteBothAsync(TimeOnly startTime, TimeOnly endTime);
    }
}
