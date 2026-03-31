using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Branch;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchService
    {
        // Lấy danh sách chi nhánh có phân trang, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        Task<PagedResult<BranchDto>> GetAllAsync(PaginationQuery query, bool includeSuspended);

        // Lấy thông tin chi nhánh theo ID, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        Task<BranchDto> GetByIdAsync(Guid id, bool includeSuspended);

        // Tạo mới chi nhánh
        Task<BranchDto> CreateAsync(CreateBranchDto dto);

        // Cập nhật thông tin chi nhánh
        Task<BranchDto> UpdateAsync(Guid id, UpdateBranchDto dto);

        // Tạm ngưng hoạt động chi nhánh
        Task SuspendAsync(Guid id);

        // Kích hoạt chi nhánh
        Task ActivateAsync(Guid id);

        // Xóa chi nhánh (chuyển trạng thái thành INACTIVE)
        Task DeleteAsync(Guid id);

        // Lấy danh sách loại sân của chi nhánh
        Task<List<BranchCourtTypeDto>> GetCourtTypesAsync(Guid branchId);

        // thêm loại sân cho chi nhánh
        Task<BranchCourtTypeDto> AddCourtTypeAsync(Guid branchId, AddCourtTypeToBranchDto dto, Guid currentUserId, string currentUserRole);

        // Xóa loại sân khỏi chi nhánh
        Task RemoveCourtTypeAsync(Guid branchId, Guid courtTypeId, Guid currentUserId, string currentUserRole);

        // Lấy danh sách dịch vụ của chi nhánh
        Task<List<BranchServiceDto>> GetServicesAsync(Guid branchId);

        // Thêm dịch vụ cho chi nhánh
        Task<BranchServiceDto> AddServiceAsync(Guid branchId, AddServiceToBranchDto dto,Guid currentUserId, string currentUserRole);

        // Cập nhật giá dịch vụ của chi nhánh
        Task<BranchServiceDto> UpdateServicePriceAsync(Guid branchId, Guid serviceId,UpdateBranchServiceDto dto,Guid currentUserId, string currentUserRole);

        // Xóa dịch vụ khỏi chi nhánh
        Task DisableServiceAsync(Guid branchId, Guid serviceId,Guid currentUserId, string currentUserRole);
    }
}
