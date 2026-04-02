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

        // tạo mới khuyến mãi
        Task<Promotion> CreateAsync(Promotion promotion);

        // cập nhật khuyến mãi
        Task UpdateAsync(Promotion promotion);

        // scheduled job — cập nhật status theo ngày
        Task UpdateExpiredStatusAsync();
    }
}
