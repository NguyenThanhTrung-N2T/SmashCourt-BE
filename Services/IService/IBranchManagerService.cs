using SmashCourt_BE.DTOs.BranchManagement;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchManagerService
    {
        /// <summary>
        /// Lấy thông tin quản lý chi nhánh hiện tại
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <returns>Thông tin quản lý chi nhánh hoặc null nếu không có</returns>
        Task<BranchManagerDto?> GetCurrentManagerAsync(Guid branchId);

        /// <summary>
        /// Gán quản lý cho chi nhánh
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="dto">Thông tin gán quản lý</param>
        /// <param name="currentUserId">ID người thực hiện</param>
        /// <returns>Thông tin quản lý đã được gán</returns>
        Task<BranchManagerDto> AssignManagerAsync(Guid branchId, AssignManagerDto dto, Guid currentUserId);

        /// <summary>
        /// Xóa quản lý khỏi chi nhánh
        /// </summary>
        /// <param name="branchId">ID chi nhánh</param>
        /// <param name="dto">Thông tin xóa quản lý</param>
        /// <param name="currentUserId">ID người thực hiện</param>
        Task RemoveManagerAsync(Guid branchId, RemoveManagerDto dto, Guid currentUserId);
    }
}