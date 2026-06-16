using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISlotInterestRepository
    {
        /// <summary>Tạo mới một slot interest record</summary>
        Task CreateAsync(SlotInterest interest);

        /// <summary>
        /// Kiểm tra email đã đăng ký interest cho slot này chưa (dedup).
        /// Match theo courtId + date + overlap startTime/endTime + email.
        /// </summary>
        Task<bool> ExistsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string email);

        /// <summary>
        /// Lấy tất cả người đã đăng ký quan tâm các slot overlap với slot vừa được giải phóng.
        /// </summary>
        Task<List<SlotInterest>> GetOverlappingSlotInterestsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        /// <summary>
        /// Xóa tất cả interest records overlap với slot vừa notify (one-shot).
        /// Trả về số rows đã xóa để dùng cho logging.
        /// </summary>
        Task<int> DeleteOverlappingSlotInterestsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        /// <summary>
        /// Xóa tất cả records đã hết hạn — dùng cho Hangfire cleanup job.
        /// Trả về số record đã xóa.
        /// </summary>
        Task<int> DeleteExpiredAsync();
    }
}

