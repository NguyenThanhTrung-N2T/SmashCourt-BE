using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IUserRepository
    {
        // lấy user theo email
        Task<User?> GetUserByEmailAsync(string email);

        // lấy user theo id
        Task<User?> GetUserByIdAsync(Guid id);

        // tạo user mới
        Task<User> CreateUserAsync(User newUser);

        // cập nhật thông tin user 
        Task UpdateUserAsync(User updateUser);

        // xóa user chưa xác thực sau 5 phút
        Task DeleteUnverifiedAsync(Guid userId);

        // tìm kiếm users với filters (dùng cho assign vào branch)
        Task<PagedResult<User>> SearchUsersAsync(UserSearchQuery query);
    }
}
