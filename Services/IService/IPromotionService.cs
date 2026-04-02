using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Promotion;

namespace SmashCourt_BE.Services.IService
{
    public interface IPromotionService
    {
        // lấy danh sách khuyến mãi có phân trang
        Task<PagedResult<PromotionDto>> GetAllAsync(PaginationQuery query);

        // lấy danh sách khuyến mãi đang hoạt động
        Task<List<PromotionDto>> GetActiveAsync();

        // lấy chi tiết khuyến mãi theo id
        Task<PromotionDto> GetByIdAsync(Guid id);

        // tạo mới khuyến mãi
        Task<PromotionDto> CreateAsync(CreatePromotionDto dto);

        // cập nhật khuyến mãi
        Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionDto dto);

        // xóa khuyến mãi
        Task DeleteAsync(Guid id);
    }

}
