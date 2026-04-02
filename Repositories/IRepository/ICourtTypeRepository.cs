using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.Interfaces;

public interface ICourtTypeRepository
{
    // Lấy danh sách loại sân (chỉ ACTIVE, phân trang)
    Task<PagedResult<CourtTypeWithCount>> GetAllAsync(int page, int pageSize);

    // Lấy thông tin chi tiết loại sân theo ID (kèm count chi nhánh và sân)
    Task<CourtTypeWithCount?> GetWithCountByIdAsync(Guid id);

    // Lấy entity thuần theo ID — dùng nội bộ cho update/delete
    Task<CourtType?> GetByIdAsync(Guid id);

    // Kiểm tra tên loại sân đã tồn tại (trừ trường hợp cập nhật cùng ID)
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);

    // Tạo mới loại sân
    Task<CourtType> CreateAsync(CourtType courtType);

    // Cập nhật loại sân
    Task UpdateAsync(CourtType courtType);

    // Kiểm tra loại sân có đang được dùng ở chi nhánh nào không
    Task<bool> IsInUseAsync(Guid id);
}