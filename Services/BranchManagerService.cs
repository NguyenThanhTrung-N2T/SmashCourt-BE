using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchManagerService : IBranchManagerService
    {
        private readonly SmashCourtContext _context;
        private readonly IUserBranchRepository _userBranchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IBranchRepository _branchRepository;

        public BranchManagerService(
            SmashCourtContext context,
            IUserBranchRepository userBranchRepository,
            IUserRepository userRepository,
            IBranchRepository branchRepository)
        {
            _context = context;
            _userBranchRepository = userBranchRepository;
            _userRepository = userRepository;
            _branchRepository = branchRepository;
        }

        public async Task<BranchManagerDto?> GetCurrentManagerAsync(Guid branchId)
        {
            // Validate branch exists
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Get current active manager
            var managerAssignment = await _context.UserBranches
                .Include(ub => ub.User)
                .FirstOrDefaultAsync(ub => 
                    ub.BranchId == branchId && 
                    ub.Role == UserBranchRole.MANAGER && 
                    ub.IsActive);

            if (managerAssignment == null)
            {
                return null;
            }

            return new BranchManagerDto
            {
                UserId = managerAssignment.UserId,
                FullName = managerAssignment.User.FullName,
                Email = managerAssignment.User.Email,
                Phone = managerAssignment.User.Phone,
                AvatarUrl = managerAssignment.User.AvatarUrl,
                AssignedAt = managerAssignment.AssignedAt,
                AssignedByName = null, // Will be enhanced when audit fields are added
                AssignedByUserId = null
            };
        }

        public async Task<BranchManagerDto> AssignManagerAsync(Guid branchId, AssignManagerDto dto, Guid currentUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Validate branch exists
                var branch = await _branchRepository.GetByIdAsync(branchId);
                if (branch == null)
                {
                    throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
                }

                // Validate user exists and is eligible
                var user = await _userRepository.GetUserByIdAsync(dto.UserId);
                if (user == null)
                {
                    throw new AppException(404, "Người dùng không tồn tại", ErrorCodes.UserNotFound);
                }

                if (user.Status != UserStatus.ACTIVE)
                {
                    throw new AppException(400, "Người dùng không ở trạng thái hoạt động", ErrorCodes.InvalidManagerUser);
                }

                // Only users with BRANCH_MANAGER role can be assigned to manage a branch
                if (user.Role != UserRole.BRANCH_MANAGER)
                {
                    throw new AppException(400, "Chỉ người dùng có vai trò BRANCH_MANAGER mới có thể được gán làm quản lý chi nhánh", ErrorCodes.InvalidManagerUser);
                }

                // Check if user is already a manager of another branch
                var existingManagerAssignment = await _context.UserBranches
                    .FirstOrDefaultAsync(ub => 
                        ub.UserId == dto.UserId && 
                        ub.Role == UserBranchRole.MANAGER && 
                        ub.IsActive);

                if (existingManagerAssignment != null)
                {
                    throw new AppException(409, "Người dùng đã là quản lý của chi nhánh khác", ErrorCodes.ManagerAlreadyExists);
                }

                // Remove current manager if exists
                var currentManager = await _userBranchRepository.GetActiveManagerByBranchIdAsync(branchId);
                if (currentManager != null)
                {
                    currentManager.IsActive = false;
                    currentManager.EndedAt = DateTime.UtcNow;
                    await _userBranchRepository.UpdateAsync(currentManager);

                    // Update previous manager's role if they have no other active assignments
                    var previousManagerUser = await _userRepository.GetUserByIdAsync(currentManager.UserId);
                    if (previousManagerUser != null)
                    {
                        var hasOtherAssignments = await _context.UserBranches
                            .AnyAsync(ub => 
                                ub.UserId == currentManager.UserId && 
                                ub.IsActive && 
                                ub.Id != currentManager.Id);

                        if (!hasOtherAssignments)
                        {
                            previousManagerUser.Role = UserRole.CUSTOMER;
                            await _userRepository.UpdateUserAsync(previousManagerUser);
                        }
                    }
                }

                // Create new manager assignment
                var newManagerAssignment = new UserBranch
                {
                    Id = Guid.NewGuid(),
                    UserId = dto.UserId,
                    BranchId = branchId,
                    Role = UserBranchRole.MANAGER,
                    IsActive = true,
                    AssignedAt = DateTime.UtcNow,
                    EndedAt = null
                };

                await _userBranchRepository.CreateAsync(newManagerAssignment);

                // Note: User must already have BRANCH_MANAGER role to be assigned
                // We don't change the user's role here - they must already be BRANCH_MANAGER

                await transaction.CommitAsync();

                // Lấy thông tin người assign (nếu có)
                var assignedByUser = await _userRepository.GetUserByIdAsync(currentUserId);

                // Return the new manager info
                return new BranchManagerDto
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    Phone = user.Phone,
                    AvatarUrl = user.AvatarUrl,
                    AssignedAt = newManagerAssignment.AssignedAt,
                    AssignedByName = assignedByUser?.FullName,
                    AssignedByUserId = currentUserId
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task RemoveManagerAsync(Guid branchId, RemoveManagerDto dto, Guid currentUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Validate branch exists
                var branch = await _branchRepository.GetByIdAsync(branchId);
                if (branch == null)
                {
                    throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
                }

                // Get current manager
                var currentManager = await _userBranchRepository.GetActiveManagerByBranchIdAsync(branchId);
                if (currentManager == null)
                {
                    throw new AppException(404, "Chi nhánh hiện tại không có quản lý", ErrorCodes.ManagerNotFound);
                }

                // Deactivate manager assignment
                currentManager.IsActive = false;
                currentManager.EndedAt = DateTime.UtcNow;
                await _userBranchRepository.UpdateAsync(currentManager);

                // Update user's role if they have no other active assignments
                var managerUser = await _userRepository.GetUserByIdAsync(currentManager.UserId);
                if (managerUser != null)
                {
                    var hasOtherAssignments = await _context.UserBranches
                        .AnyAsync(ub => 
                            ub.UserId == currentManager.UserId && 
                            ub.IsActive && 
                            ub.Id != currentManager.Id);

                    if (!hasOtherAssignments)
                    {
                        managerUser.Role = UserRole.CUSTOMER;
                        await _userRepository.UpdateUserAsync(managerUser);
                    }
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}