using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Branch;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchService
    {
        // Lấy danh sách chi nhánh có phân trang, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        Task<PagedResult<BranchDto>> GetAllAsync(PaginationQuery query, bool includeSuspended);

        // Lấy danh sách thông tin cơ bản của chi nhánh đang hoạt động cho public
        Task<PagedResult<BranchBasicDto>> GetAllBasicAsync(PaginationQuery query);

        // Lấy thông tin chi nhánh theo ID, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        Task<BranchDto> GetByIdAsync(Guid id, bool includeSuspended);

        // Lấy thông tin cơ bản của chi nhánh đang hoạt động theo ID cho public
        Task<BranchBasicDto> GetBasicByIdAsync(Guid id);

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

        // Lấy danh sách dịch vụ của chi nhánh kèm phân trang và phân quyền theo requester
        Task<PagedResult<BranchServiceDto>> GetServicesAsync(Guid? requestedBranchId, PaginationQuery query, Guid currentUserId, string currentUserRole);

        // Thêm dịch vụ cho chi nhánh
        Task<BranchServiceDto> AddServiceAsync(Guid? requestedBranchId, AddServiceToBranchDto dto, Guid currentUserId, string currentUserRole);

        // Cập nhật giá dịch vụ của chi nhánh
        Task<BranchServiceDto> UpdateServicePriceAsync(Guid? requestedBranchId, Guid serviceId, UpdateBranchServiceDto dto, Guid currentUserId, string currentUserRole);

        // Xóa dịch vụ khỏi chi nhánh
        Task DisableServiceAsync(Guid? requestedBranchId, Guid serviceId, Guid currentUserId, string currentUserRole);
    }
}
