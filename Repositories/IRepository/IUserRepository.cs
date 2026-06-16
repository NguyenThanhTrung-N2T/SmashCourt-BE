using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.DTOs.UserManagement;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IUserRepository
    {
        // lấy user theo email
        Task<User?> GetUserByEmailAsync(string email);

        // lấy user theo id
        Task<User?> GetUserByIdAsync(Guid id);

        // lấy user theo id với include UserBranches và Branch
        Task<User?> GetUserByIdWithBranchAsync(Guid id);

        // lấy user theo id với include TẤT CẢ UserBranches (kể cả inactive) và Branch
        // Dùng cho ActivateUserAsync để restore inactive branch
        Task<User?> GetUserByIdWithAllBranchesAsync(Guid id);

        // tạo user mới
        Task<User> CreateUserAsync(User newUser);

        // cập nhật thông tin user 
        Task UpdateUserAsync(User updateUser);

        // xóa user chưa xác thực sau 5 phút
        Task DeleteUnverifiedAsync(Guid userId);

        // tìm kiếm users với filters (dùng cho assign vào branch)
        Task<PagedResult<User>> SearchUsersAsync(UserSearchQuery query);

        // lấy danh sách users với filter và phân trang (dùng cho User Management)
        Task<PagedResult<User>> GetUsersAsync(UserListQuery query);

        // đếm số BRANCH_MANAGER ACTIVE trong chi nhánh (dùng để check trước khi lock/deactivate)
        Task<int> CountActiveBranchManagersAsync(Guid branchId);

        // kiểm tra email đã tồn tại chưa (case-insensitive)
        Task<bool> IsEmailExistsAsync(string email, Guid? excludeUserId = null);
    }
}
