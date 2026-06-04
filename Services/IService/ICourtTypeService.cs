using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.DTOs.Branch;

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

    // Lấy danh sách loại sân của chi nhánh
    Task<List<BranchCourtTypeDto>> GetCourtTypesAsync(Guid? requestedBranchId, Guid currentUserId, string currentUserRole);

    // Thêm loại sân cho chi nhánh (bật loại sân)
    Task<BranchCourtTypeDto> AddCourtTypeAsync(Guid? requestedBranchId, AddCourtTypeToBranchDto dto, Guid currentUserId, string currentUserRole);

    // Xóa loại sân khỏi chi nhánh (tắt loại sân)
    Task RemoveCourtTypeAsync(Guid? requestedBranchId, Guid courtTypeId, Guid currentUserId, string currentUserRole);
}