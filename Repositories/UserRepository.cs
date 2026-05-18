using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.DTOs.UserManagement;
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

        // lấy user theo id với include UserBranches và Branch
        // LƯU Ý: Method này sử dụng EF tracking để có thể update user sau đó
        // Nếu cần read-only data, caller nên xem xét dùng AsNoTracking()
        // Filter .Where(ub => ub.IsActive) trong Include chỉ filter navigation property,
        // KHÔNG filter user entity (user vẫn được trả về dù không có active branch)
        public async Task<User?> GetUserByIdWithBranchAsync(Guid id)
        {
            return await _context.Users
                .Include(u => u.UserBranches.Where(ub => ub.IsActive))
                    .ThenInclude(ub => ub.Branch)
                .FirstOrDefaultAsync(u => u.Id == id);
        }

        // lấy user theo id với include TẤT CẢ UserBranches (kể cả inactive) và Branch
        // Dùng cho ActivateUserAsync để có thể tìm và restore inactive branch
        public async Task<User?> GetUserByIdWithAllBranchesAsync(Guid id)
        {
            return await _context.Users
                .Include(u => u.UserBranches)  // KHÔNG filter - lấy tất cả
                    .ThenInclude(ub => ub.Branch)
                .FirstOrDefaultAsync(u => u.Id == id);
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
                // Loại trừ CUSTOMER — endpoint này chỉ dùng để gán vào chi nhánh (manager/staff)
                .Where(u => u.Role != UserRole.CUSTOMER)
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

        // lấy danh sách users với filter và phân trang (dùng cho User Management)
        public async Task<PagedResult<User>> GetUsersAsync(UserListQuery query)
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

            // Áp dụng filter theo chi nhánh
            if (query.BranchId.HasValue)
            {
                usersQuery = usersQuery.Where(u =>
                    u.UserBranches.Any(ub => ub.BranchId == query.BranchId.Value && ub.IsActive));
            }

            // Lấy tổng số record trước khi phân trang
            var totalItems = await usersQuery.CountAsync();

            // Áp dụng sắp xếp
            usersQuery = query.SortBy.ToLower() switch
            {
                "fullname" => query.SortOrder.ToLower() == "asc"
                    ? usersQuery.OrderBy(u => u.FullName)
                    : usersQuery.OrderByDescending(u => u.FullName),
                "email" => query.SortOrder.ToLower() == "asc"
                    ? usersQuery.OrderBy(u => u.Email)
                    : usersQuery.OrderByDescending(u => u.Email),
                _ => query.SortOrder.ToLower() == "asc"
                    ? usersQuery.OrderBy(u => u.CreatedAt)
                    : usersQuery.OrderByDescending(u => u.CreatedAt)
            };

            // Áp dụng phân trang
            var users = await usersQuery
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

        // đếm số BRANCH_MANAGER ACTIVE trong chi nhánh (dùng để check trước khi lock/deactivate)
        public async Task<int> CountActiveBranchManagersAsync(Guid branchId)
        {
            return await _context.UserBranches
                .Where(ub =>
                    ub.BranchId == branchId &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive &&
                    ub.User.Status == UserStatus.ACTIVE)
                .CountAsync();
        }

        // kiểm tra email đã tồn tại chưa (case-insensitive)
        public async Task<bool> IsEmailExistsAsync(string email, Guid? excludeUserId = null)
        {
            var normalizedEmail = email.Trim().ToLower();
            var query = _context.Users.Where(u => u.Email.ToLower() == normalizedEmail);

            if (excludeUserId.HasValue)
            {
                query = query.Where(u => u.Id != excludeUserId.Value);
            }

            return await query.AnyAsync();
        }
    }
}
