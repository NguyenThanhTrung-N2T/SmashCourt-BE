using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchStaffService
    {
        /// <summary>
        /// Lấy danh sách nhân viên của chi nhánh với bộ lọc
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="query">Bộ lọc và phân trang</param>
        /// <returns>Danh sách nhân viên có phân trang</returns>
        Task<PagedResult<BranchStaffDto>> GetStaffAsync(Guid branchId, StaffFilterQuery query);

        /// <summary>
        /// Thêm nhân viên vào chi nhánh
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="dto">Thông tin thêm nhân viên</param>
        /// <param name="currentUserId">ID người thực hiện</param>
        /// <returns>Thông tin nhân viên đã được thêm</returns>
        Task<BranchStaffDto> AddStaffAsync(Guid branchId, AddStaffDto dto, Guid currentUserId);

        /// <summary>
        /// Xóa nhân viên khỏi chi nhánh
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="userId">ID nhân viên</param>
        /// <param name="dto">Thông tin xóa nhân viên</param>
        /// <param name="currentUserId">ID người thực hiện</param>
        Task RemoveStaffAsync(Guid branchId, Guid userId, RemoveStaffDto dto, Guid currentUserId);

        /// <summary>
        /// Thực hiện các thao tác hàng loạt với nhân viên
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="dto">Thông tin thao tác hàng loạt</param>
        /// <param name="currentUserId">ID người thực hiện</param>
        /// <returns>Kết quả thao tác hàng loạt</returns>
        Task<BulkStaffOperationResultDto> BulkStaffOperationAsync(Guid branchId, BulkStaffOperationDto dto, Guid currentUserId);
    }
}