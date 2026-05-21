using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISlotInterestRepository
    {
        /// <summary>Táº¡o má»›i má»™t slot interest record</summary>
        Task CreateAsync(SlotInterest interest);

        /// <summary>
        /// Kiá»ƒm tra email Ä‘Ã£ Ä‘Äƒng kÃ½ interest cho slot nÃ y chÆ°a (dedup).
        /// Match theo courtId + date + overlap startTime/endTime + email.
        /// </summary>
        Task<bool> ExistsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string email);

        /// <summary>
        /// Lấy tất cả người đã đăng ký quan tâm các slot overlap với slot vừa được giải phóng.
        /// </summary>
        Task<List<SlotInterest>> GetOverlappingSlotInterestsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        /// <summary>
        /// Xóa tất cả interest records overlap với slot vừa notify (one-shot).
        /// </summary>
        Task DeleteOverlappingSlotInterestsAsync(Guid courtId, DateOnly date, TimeOnly startTime, TimeOnly endTime);

        /// <summary>
        /// XÃ³a táº¥t cáº£ records Ä‘Ã£ háº¿t háº¡n â€” dÃ¹ng cho Hangfire cleanup job.
        /// Tráº£ vá» sá»‘ record Ä‘Ã£ xÃ³a.
        /// </summary>
        Task<int> DeleteExpiredAsync();
    }
}

