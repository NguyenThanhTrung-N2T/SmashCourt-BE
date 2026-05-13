using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IPromotionRepository
    {
        // lấy tất cả khuyến mãi có phân trang
        Task<PagedResult<Promotion>> GetAllAsync(int page, int pageSize);

        // lấy tất cả khuyến mãi đang ACTIVE (dùng khi đặt sân)
        Task<List<Promotion>> GetActiveAsync();

        // lấy khuyến mãi theo id
        Task<Promotion?> GetByIdAsync(Guid id);

        // lấy khuyến mãi theo id với conditions
        Task<Promotion?> GetByIdWithConditionsAsync(Guid id);

        // kiểm tra code đã tồn tại
        Task<bool> CodeExistsAsync(string code, Guid? excludeId = null);

        // tạo mới khuyến mãi
        Task<Promotion> CreateAsync(Promotion promotion);

        // cập nhật khuyến mãi
        Task UpdateAsync(Promotion promotion);

        // xóa conditions của promotion
        Task RemoveConditionsAsync(Guid promotionId);

        // scheduled job — cập nhật status theo ngày
        Task UpdateExpiredStatusAsync();
    }
}
