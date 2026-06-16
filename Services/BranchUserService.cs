using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchUserService : IBranchUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IUserBranchRepository _userBranchRepository;

        public BranchUserService(
            IUserRepository userRepository,
            IUserBranchRepository userBranchRepository)
        {
            _userRepository = userRepository;
            _userBranchRepository = userBranchRepository;
        }

        public async Task<PagedResult<UserSearchResultDto>> SearchUsersAsync(UserSearchQuery query)
        {
            // Lấy users từ repository (đã bao gồm filters và pagination)
            var pagedResult = await _userRepository.SearchUsersAsync(query);

            // Map sang DTOs với kiểm tra điều kiện đủ tiêu chuẩn
            var userDtos = pagedResult.Items.Select(user => new UserSearchResultDto
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
                IsEligibleForManager = IsEligibleForManager(user),
                IsEligibleForStaff = IsEligibleForStaff(user)
            }).ToList();

            return new PagedResult<UserSearchResultDto>
            {
                Items = userDtos,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalItems = pagedResult.TotalItems
            };
        }

        public async Task<List<UserBranchAssignmentDto>> GetUserAssignmentsAsync(Guid userId)
        {
            var userBranches = await _userBranchRepository.GetUserAssignmentsAsync(userId);

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
        /// Kiểm tra xem user có đủ điều kiện được gán làm manager không
        /// Quy tắc nghiệp vụ:
        /// - User phải có status ACTIVE
        /// - User phải có role BRANCH_MANAGER (chỉ BRANCH_MANAGER mới có thể quản lý chi nhánh)
        /// - User không được đang quản lý chi nhánh khác
        /// </summary>
        private static bool IsEligibleForManager(Models.Entities.User user)
        {
            if (user.Status != UserStatus.ACTIVE)
                return false;

            if (user.Role != UserRole.BRANCH_MANAGER)
                return false;

            // Kiểm tra xem user đã đang quản lý chi nhánh khác chưa
            var isCurrentlyManager = user.UserBranches
                .Any(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive);

            return !isCurrentlyManager;
        }

        /// <summary>
        /// Kiểm tra xem user có đủ điều kiện được gán làm staff không
        /// Quy tắc nghiệp vụ:
        /// - User phải có status ACTIVE
        /// - User có thể là CUSTOMER, STAFF, hoặc BRANCH_MANAGER (manager cũng có thể làm staff ở chi nhánh khác)
        /// </summary>
        private static bool IsEligibleForStaff(Models.Entities.User user)
        {
            if (user.Status != UserStatus.ACTIVE)
                return false;

            // OWNER không nên được gán làm staff
            return user.Role != UserRole.OWNER;
        }
    }
}
