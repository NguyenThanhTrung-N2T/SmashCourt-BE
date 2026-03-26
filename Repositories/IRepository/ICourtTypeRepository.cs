using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.Interfaces;

public interface ICourtTypeRepository
{
    // Lấy danh sách loại sân (chỉ ACTIVE, phân trang)
    Task<PagedResult<CourtTypeWithCount>> GetAllAsync(int page, int pageSize);

    // Lấy thông tin chi tiết loại sân theo ID
    Task<CourtType?> GetByIdAsync(Guid id);

    // Kiểm tra tên loại sân đã tồn tại (trừ trường hợp cập nhật cùng ID)
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);

    // Tạo mới loại sân
    Task<CourtType> CreateAsync(CourtType courtType);

    // Cập nhật loại sân
    Task UpdateAsync(CourtType courtType);

    // Vô hiệu hóa (soft delete) loại sân
    Task<bool> IsInUseAsync(Guid id);
}