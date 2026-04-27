using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly SmashCourtContext _context;
        public UserRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // lấy user theo email
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        }

        // tạo user mới 
        public async Task<User> CreateUserAsync(User newUser)
        {
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            return newUser;
        }

        // cập nhật thông tin user 
        public async Task UpdateUserAsync(User updateUser)
        {
            _context.Users.Update(updateUser);
            await _context.SaveChangesAsync();
        }

        // lấy user theo id
        public async Task<User?> GetUserByIdAsync(Guid id)
        {
            return await _context.Users.FindAsync(id);
        }

        // xóa user chưa xác thực sau 5 phút
        public async Task DeleteUnverifiedAsync(Guid userId)
        {
            await _context.Users
                .Where(u => u.Id == userId && !u.IsEmailVerified)
                .ExecuteDeleteAsync();
        }

        // tìm kiếm users với filters (dùng cho assign vào branch)
        public async Task<PagedResult<User>> SearchUsersAsync(UserSearchQuery query)
        {
            var usersQuery = _context.Users
                .Include(u => u.UserBranches.Where(ub => ub.IsActive))
                    .ThenInclude(ub => ub.Branch)
                .AsQueryable();

            // Áp dụng filter tìm kiếm theo tên, email, phone
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchTerm = query.SearchTerm.Trim().ToLower();
                usersQuery = usersQuery.Where(u =>
                    u.FullName.ToLower().Contains(searchTerm) ||
                    u.Email.ToLower().Contains(searchTerm) ||
                    (u.Phone != null && u.Phone.ToLower().Contains(searchTerm)));
            }

            // Áp dụng filter theo role
            if (query.Role.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.Role == query.Role.Value);
            }

            // Áp dụng filter theo status
            if (query.Status.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.Status == query.Status.Value);
            }

            // Loại trừ users đã được gán vào chi nhánh cụ thể
            if (query.ExcludeAssignedToBranch == true && query.ExcludeBranchId.HasValue)
            {
                usersQuery = usersQuery.Where(u =>
                    !u.UserBranches.Any(ub => ub.BranchId == query.ExcludeBranchId.Value && ub.IsActive));
            }

            // Áp dụng filter điều kiện đủ tiêu chuẩn ở database level để tăng hiệu suất
            if (query.EligibleForManager == true)
            {
                // Chỉ user có role BRANCH_MANAGER mới có thể được gán làm quản lý chi nhánh
                usersQuery = usersQuery.Where(u =>
                    u.Status == UserStatus.ACTIVE &&
                    u.Role == UserRole.BRANCH_MANAGER &&
                    !u.UserBranches.Any(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive));
            }

            if (query.EligibleForStaff == true)
            {
                usersQuery = usersQuery.Where(u =>
                    u.Status == UserStatus.ACTIVE &&
                    u.Role != UserRole.OWNER);
            }

            // Lấy tổng số record trước khi phân trang
            var totalItems = await usersQuery.CountAsync();

            // Áp dụng phân trang
            var users = await usersQuery
                .OrderBy(u => u.FullName)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<User>
            {
                Items = users,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = totalItems
            };
        }
    }
}
