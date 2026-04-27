using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class UserSearchService : IUserSearchService
    {
        private readonly SmashCourtContext _context;
        private readonly ILogger<UserSearchService> _logger;

        public UserSearchService(SmashCourtContext context, ILogger<UserSearchService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PagedResult<UserSearchResultDto>> SearchUsersAsync(UserSearchQuery query)
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

            // Map sang DTOs với kiểm tra điều kiện đủ tiêu chuẩn
            var userDtos = users.Select(user => new UserSearchResultDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl,
                CurrentRole = user.Role,
                Status = user.Status,
                CurrentAssignments = user.UserBranches
                    .Where(ub => ub.IsActive)
                    .Select(ub => new UserBranchSummaryDto
                    {
                        BranchId = ub.BranchId,
                        BranchName = ub.Branch.Name,
                        Role = ub.Role,
                        IsActive = ub.IsActive
                    }).ToList(),
                IsEligibleForManager = IsEligibleForManagerSync(user),
                IsEligibleForStaff = IsEligibleForStaff(user)
            }).ToList();

            return new PagedResult<UserSearchResultDto>
            {
                Items = userDtos,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = totalItems
            };
        }

        public async Task<List<UserBranchAssignmentDto>> GetUserAssignmentsAsync(Guid userId)
        {
            var userBranches = await _context.UserBranches
                .Include(ub => ub.Branch)
                .Where(ub => ub.UserId == userId)
                .OrderByDescending(ub => ub.AssignedAt)
                .ToListAsync();

            return userBranches.Select(ub => new UserBranchAssignmentDto
            {
                BranchId = ub.BranchId,
                BranchName = ub.Branch.Name,
                BranchAddress = ub.Branch.Address,
                Role = ub.Role,
                IsActive = ub.IsActive,
                AssignedAt = ub.AssignedAt,
                EndedAt = ub.EndedAt
            }).ToList();
        }

        /// <summary>
        /// Kiểm tra xem user có đủ điều kiện được gán làm manager không (phiên bản synchronous cho entities đã load)
        /// Quy tắc nghiệp vụ:
        /// - User phải có status ACTIVE
        /// - User phải có role BRANCH_MANAGER (chỉ BRANCH_MANAGER mới có thể quản lý chi nhánh)
        /// - User không được đang quản lý chi nhánh khác
        /// </summary>
        private static bool IsEligibleForManagerSync(Models.Entities.User user)
        {
            // Phải ở trạng thái active
            if (user.Status != UserStatus.ACTIVE)
                return false;

            // Phải có role BRANCH_MANAGER - chỉ BRANCH_MANAGER mới có thể được gán quản lý chi nhánh
            if (user.Role != UserRole.BRANCH_MANAGER)
                return false;

            // Kiểm tra xem user đã đang quản lý chi nhánh khác chưa (dùng UserBranches đã load)
            var isCurrentlyManager = user.UserBranches
                .Any(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive);

            if (isCurrentlyManager)
                return false;

            return true;
        }

        /// <summary>
        /// Kiểm tra xem user có đủ điều kiện được gán làm staff không
        /// Quy tắc nghiệp vụ:
        /// - User phải có status ACTIVE
        /// - User có thể là CUSTOMER, STAFF, hoặc BRANCH_MANAGER (manager cũng có thể làm staff ở chi nhánh khác)
        /// </summary>
        private static bool IsEligibleForStaff(Models.Entities.User user)
        {
            // Phải ở trạng thái active
            if (user.Status != UserStatus.ACTIVE)
                return false;

            // OWNER thường không nên được gán làm staff
            return user.Role != UserRole.OWNER;
        }
    }
}