using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;

namespace SmashCourt_BE.Services.IService;

public interface ICourtTypeService
{
    // Lấy danh sách loại sân (chỉ ACTIVE, phân trang)
    Task<PagedResult<CourtTypeDto>> GetAllCourtTypesAsync(PaginationQuery query);

    // Lấy thông tin chi tiết loại sân theo ID
    Task<CourtTypeDto> GetByIdAsync(Guid id);

    // Tạo mới loại sân
    Task<CourtTypeDto> CreateAsync(CreateCourtTypeDto dto);

    // Cập nhật loại sân (có thể cập nhật tên, mô tả, trạng thái)
    Task<CourtTypeDto> UpdateAsync(Guid id, UpdateCourtTypeDto dto);

    // Vô hiệu hóa (soft delete) loại sân
    Task DeleteAsync(Guid id);
}