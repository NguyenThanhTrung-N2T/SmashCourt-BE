using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IUserBranchRepository
    {
        // lấy thông tin gán chi nhánh của user đang hoạt động
        Task<UserBranch?> GetActiveByUserIdAsync(Guid userId);

        // lấy thông tin quản lý chi nhánh đang hoạt động theo branchId
        Task<UserBranch?> GetActiveManagerByBranchIdAsync(Guid branchId);

        // tạo mới gán chi nhánh cho user
        Task<UserBranch> CreateAsync(UserBranch userBranch);

        // cập nhật gán chi nhánh cho user
        Task UpdateAsync(UserBranch userBranch);

        // kiểm tra xem user có đang gán chi nhánh nào không
        Task<bool> IsUserInBranchAsync(Guid userId, Guid branchId);

        // lấy assignment MANAGER active của user (dùng để check conflict khi assign manager mới)
        Task<UserBranch?> GetActiveManagerAssignmentByUserIdAsync(Guid userId);
    }
}
