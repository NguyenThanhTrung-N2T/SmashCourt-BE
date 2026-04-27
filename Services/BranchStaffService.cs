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
    public class BranchStaffService : IBranchStaffService
    {
        private readonly SmashCourtContext _context;
        private readonly IUserBranchRepository _userBranchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IBranchRepository _branchRepository;

        public BranchStaffService(
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

        public async Task<PagedResult<BranchStaffDto>> GetStaffAsync(Guid branchId, StaffFilterQuery query)
        {
            // Validate branch exists
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Build query for staff members
            var staffQuery = _context.UserBranches
                .Include(ub => ub.User)
                .Where(ub => ub.BranchId == branchId && ub.Role == UserBranchRole.STAFF);

            // Apply filters
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

            // Get total count
            var totalItems = await staffQuery.CountAsync();

            // Apply pagination and ordering
            var items = await staffQuery
                .OrderByDescending(ub => ub.AssignedAt)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(ub => new BranchStaffDto
                {
                    UserId = ub.UserId,
                    FullName = ub.User.FullName,
                    Email = ub.User.Email,
                    Phone = ub.User.Phone,
                    AvatarUrl = ub.User.AvatarUrl,
                    Role = ub.Role,
                    IsActive = ub.IsActive,
                    AssignedAt = ub.AssignedAt,
                    EndedAt = ub.EndedAt,
                    AssignedByName = null // Will be enhanced when audit fields are added
                })
                .ToListAsync();

            return new PagedResult<BranchStaffDto>
            {
                Items = items,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = totalItems
            };
        }

        public async Task<BranchStaffDto> AddStaffAsync(Guid branchId, AddStaffDto dto, Guid currentUserId)
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
                    throw new AppException(400, "Người dùng không ở trạng thái hoạt động", ErrorCodes.InvalidStaffUser);
                }

                // Check if user is already assigned to this branch
                var existingAssignment = await _context.UserBranches
                    .FirstOrDefaultAsync(ub => 
                        ub.UserId == dto.UserId && 
                        ub.BranchId == branchId && 
                        ub.IsActive);

                if (existingAssignment != null)
                {
                    throw new AppException(409, "Người dùng đã được gán vào chi nhánh này", ErrorCodes.StaffAlreadyExists);
                }

                // Create new staff assignment
                var newStaffAssignment = new UserBranch
                {
                    Id = Guid.NewGuid(),
                    UserId = dto.UserId,
                    BranchId = branchId,
                    Role = dto.Role,
                    IsActive = true,
                    AssignedAt = DateTime.UtcNow,
                    EndedAt = null
                };

                await _userBranchRepository.CreateAsync(newStaffAssignment);

                // Update user's global role to STAFF if currently CUSTOMER
                if (user.Role == UserRole.CUSTOMER)
                {
                    user.Role = UserRole.STAFF;
                    await _userRepository.UpdateUserAsync(user);
                }

                await transaction.CommitAsync();

                // Return the new staff info
                return new BranchStaffDto
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    Phone = user.Phone,
                    AvatarUrl = user.AvatarUrl,
                    Role = newStaffAssignment.Role,
                    IsActive = newStaffAssignment.IsActive,
                    AssignedAt = newStaffAssignment.AssignedAt,
                    EndedAt = newStaffAssignment.EndedAt,
                    AssignedByName = null // Will be enhanced when audit fields are added
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task RemoveStaffAsync(Guid branchId, Guid userId, RemoveStaffDto dto, Guid currentUserId)
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

                // Get current staff assignment
                var staffAssignment = await _context.UserBranches
                    .FirstOrDefaultAsync(ub => 
                        ub.UserId == userId && 
                        ub.BranchId == branchId && 
                        ub.Role == UserBranchRole.STAFF && 
                        ub.IsActive);

                if (staffAssignment == null)
                {
                    throw new AppException(404, "Nhân viên không được tìm thấy trong chi nhánh này", ErrorCodes.StaffNotFound);
                }

                // Deactivate staff assignment
                staffAssignment.IsActive = false;
                staffAssignment.EndedAt = DateTime.UtcNow;
                await _userBranchRepository.UpdateAsync(staffAssignment);

                // Update user's role if they have no other active assignments
                var staffUser = await _userRepository.GetUserByIdAsync(userId);
                if (staffUser != null)
                {
                    var hasOtherAssignments = await _context.UserBranches
                        .AnyAsync(ub => 
                            ub.UserId == userId && 
                            ub.IsActive && 
                            ub.Id != staffAssignment.Id);

                    if (!hasOtherAssignments)
                    {
                        staffUser.Role = UserRole.CUSTOMER;
                        await _userRepository.UpdateUserAsync(staffUser);
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

        public async Task<BulkStaffOperationResultDto> BulkStaffOperationAsync(Guid branchId, BulkStaffOperationDto dto, Guid currentUserId)
        {
            // Validate branch exists
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            var result = new BulkStaffOperationResultDto();

            foreach (var userId in dto.UserIds)
            {
                try
                {
                    switch (dto.Operation)
                    {
                        case BulkOperationType.ADD_STAFF:
                            await AddStaffAsync(branchId, new AddStaffDto 
                            { 
                                UserId = userId, 
                                Role = dto.NewRole ?? UserBranchRole.STAFF,
                                Reason = dto.Reason,
                                Notes = dto.Notes
                            }, currentUserId);
                            result.SuccessCount++;
                            break;

                        case BulkOperationType.REMOVE_STAFF:
                            await RemoveStaffAsync(branchId, userId, new RemoveStaffDto 
                            { 
                                Reason = dto.Reason,
                                Notes = dto.Notes
                            }, currentUserId);
                            result.SuccessCount++;
                            break;

                        case BulkOperationType.CHANGE_ROLE:
                            if (!dto.NewRole.HasValue)
                            {
                                throw new AppException(400, "NewRole là bắt buộc cho thao tác thay đổi vai trò", ErrorCodes.InvalidBulkOperation);
                            }
                            await ChangeStaffRoleAsync(branchId, userId, dto.NewRole.Value, currentUserId);
                            result.SuccessCount++;
                            break;

                        default:
                            throw new AppException(400, "Thao tác không hợp lệ", ErrorCodes.InvalidBulkOperation);
                    }
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    
                    // Get user name for error reporting
                    var user = await _userRepository.GetUserByIdAsync(userId);
                    var userName = user?.FullName ?? userId.ToString();
                    
                    result.Errors.Add(new BulkOperationError
                    {
                        UserId = userId,
                        UserName = userName,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return result;
        }

        private async Task ChangeStaffRoleAsync(Guid branchId, Guid userId, UserBranchRole newRole, Guid currentUserId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Get current staff assignment
                var staffAssignment = await _context.UserBranches
                    .FirstOrDefaultAsync(ub => 
                        ub.UserId == userId && 
                        ub.BranchId == branchId && 
                        ub.IsActive);

                if (staffAssignment == null)
                {
                    throw new AppException(404, "Nhân viên không được tìm thấy trong chi nhánh này", ErrorCodes.StaffNotFound);
                }

                // Update role
                staffAssignment.Role = newRole;
                await _userBranchRepository.UpdateAsync(staffAssignment);

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