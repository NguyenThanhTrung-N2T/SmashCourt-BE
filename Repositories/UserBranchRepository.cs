using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;
namespace SmashCourt_BE.Repositories
{
    public class UserBranchRepository : IUserBranchRepository
    {
        private readonly SmashCourtContext _context;

        public UserBranchRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lấy thông tin gán chi nhánh của user đang hoạt động
        public async Task<UserBranch?> GetActiveByUserIdAsync(Guid userId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.UserId == userId &&
                    ub.IsActive);
        }

        // Lấy manager active của chi nhánh
        public async Task<UserBranch?> GetActiveManagerByBranchIdAsync(Guid branchId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.BranchId == branchId &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive);
        }

        // Lấy manager active của chi nhánh (bao gồm User navigation)
        public async Task<UserBranch?> GetActiveManagerWithUserAsync(Guid branchId)
        {
            return await _context.UserBranches
                .Include(ub => ub.User)
                .FirstOrDefaultAsync(ub =>
                    ub.BranchId == branchId &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive);
        }

        // tạo mới gán chi nhánh cho user
        public async Task<UserBranch> CreateAsync(UserBranch userBranch)
        {
            _context.UserBranches.Add(userBranch);
            await _context.SaveChangesAsync();
            return userBranch;
        }

        // cập nhật thông tin gán chi nhánh của user
        public async Task UpdateAsync(UserBranch userBranch)
        {
            _context.UserBranches.Update(userBranch);
            await _context.SaveChangesAsync();
        }

        // kiểm tra xem user có đang gán vào chi nhánh nào không — dùng để xác thực quyền truy cập hoặc hiển thị thông tin chi nhánh của user
        public async Task<bool> IsUserInBranchAsync(Guid userId, Guid branchId)
        {
            return await _context.UserBranches
                .AnyAsync(ub =>
                    ub.UserId == userId &&
                    ub.BranchId == branchId &&
                    ub.IsActive);
        }

        // lấy assignment MANAGER active của user — chỉ check role MANAGER, không block STAFF assignment
        public async Task<UserBranch?> GetActiveManagerAssignmentByUserIdAsync(Guid userId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.UserId == userId &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive);
        }

        // kiểm tra user có assignment active nào khác không (dùng để check trước khi downgrade role)
        public async Task<bool> HasOtherActiveAssignmentsAsync(Guid userId, Guid excludeAssignmentId)
        {
            return await _context.UserBranches
                .AnyAsync(ub =>
                    ub.UserId == userId &&
                    ub.IsActive &&
                    ub.Id != excludeAssignmentId);
        }

        // lấy danh sách staff của chi nhánh với filter và phân trang
        public async Task<PagedResult<UserBranch>> GetStaffByBranchAsync(Guid branchId, StaffFilterQuery query)
        {
            var staffQuery = _context.UserBranches
                .Include(ub => ub.User)
                .Where(ub => ub.BranchId == branchId && ub.Role == UserBranchRole.STAFF);

            // Áp dụng filters
            if (query.IsActive.HasValue)
            {
                staffQuery = staffQuery.Where(ub => ub.IsActive == query.IsActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchTerm = query.SearchTerm.ToLower();
                staffQuery = staffQuery.Where(ub =>
                    ub.User.FullName.ToLower().Contains(searchTerm) ||
                    ub.User.Email.ToLower().Contains(searchTerm) ||
                    (ub.User.Phone != null && ub.User.Phone.Contains(searchTerm)));
            }

            // Lấy tổng số record
            var totalItems = await staffQuery.CountAsync();

            // Áp dụng phân trang và sắp xếp
            var items = await staffQuery
                .OrderByDescending(ub => ub.AssignedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<UserBranch>
            {
                Items = items,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = totalItems
            };
        }

        // lấy staff assignment cụ thể
        public async Task<UserBranch?> GetStaffAssignmentAsync(Guid userId, Guid branchId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.UserId == userId &&
                    ub.BranchId == branchId &&
                    ub.Role == UserBranchRole.STAFF &&
                    ub.IsActive);
        }

        // kiểm tra user đã được assign vào chi nhánh chưa (bất kỳ role nào)
        public async Task<UserBranch?> GetActiveAssignmentAsync(Guid userId, Guid branchId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.UserId == userId &&
                    ub.BranchId == branchId &&
                    ub.IsActive);
        }

        // lấy danh sách assignments của user (tất cả branches)
        public async Task<List<UserBranch>> GetUserAssignmentsAsync(Guid userId)
        {
            return await _context.UserBranches
                .Include(ub => ub.Branch)
                .Where(ub => ub.UserId == userId)
                .OrderByDescending(ub => ub.AssignedAt)
                .ToListAsync();
        }
    }
}
