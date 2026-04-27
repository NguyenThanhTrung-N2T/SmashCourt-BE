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

            // Apply search term filter (name, email, phone)
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchTerm = query.SearchTerm.Trim().ToLower();
                usersQuery = usersQuery.Where(u =>
                    u.FullName.ToLower().Contains(searchTerm) ||
                    u.Email.ToLower().Contains(searchTerm) ||
                    (u.Phone != null && u.Phone.ToLower().Contains(searchTerm)));
            }

            // Apply role filter
            if (query.Role.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.Role == query.Role.Value);
            }

            // Apply status filter
            if (query.Status.HasValue)
            {
                usersQuery = usersQuery.Where(u => u.Status == query.Status.Value);
            }

            // Exclude users already assigned to specific branch
            if (query.ExcludeAssignedToBranch == true && query.ExcludeBranchId.HasValue)
            {
                usersQuery = usersQuery.Where(u =>
                    !u.UserBranches.Any(ub => ub.BranchId == query.ExcludeBranchId.Value && ub.IsActive));
            }

            // Apply eligibility filters at database level for better performance
            if (query.EligibleForManager == true)
            {
                // Only BRANCH_MANAGER role can be assigned to manage a branch
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

            // Get total count before pagination
            var totalItems = await usersQuery.CountAsync();

            // Apply pagination
            var users = await usersQuery
                .OrderBy(u => u.FullName)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            // Map to DTOs with eligibility checks
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
        /// Check if user is eligible to be assigned as a manager (synchronous version for loaded entities)
        /// Business rules:
        /// - User must be ACTIVE status
        /// - User must have BRANCH_MANAGER role (only BRANCH_MANAGER can manage branches)
        /// - User must not already be managing another branch
        /// </summary>
        private static bool IsEligibleForManagerSync(Models.Entities.User user)
        {
            // Must be active
            if (user.Status != UserStatus.ACTIVE)
                return false;

            // Must have BRANCH_MANAGER role - only BRANCH_MANAGER can be assigned to manage a branch
            if (user.Role != UserRole.BRANCH_MANAGER)
                return false;

            // Check if user is already managing another branch (using loaded UserBranches)
            var isCurrentlyManager = user.UserBranches
                .Any(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive);

            if (isCurrentlyManager)
                return false;

            return true;
        }

        /// <summary>
        /// Check if user is eligible to be assigned as staff
        /// Business rules:
        /// - User must be ACTIVE status
        /// - User can be CUSTOMER, STAFF, or BRANCH_MANAGER (managers can also be staff at other branches)
        /// </summary>
        private static bool IsEligibleForStaff(Models.Entities.User user)
        {
            // Must be active
            if (user.Status != UserStatus.ACTIVE)
                return false;

            // OWNER users typically shouldn't be assigned as staff
            return user.Role != UserRole.OWNER;
        }
    }
}