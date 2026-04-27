using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchStaffService : IBranchStaffService
    {
        private readonly IUserBranchRepository _userBranchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IBranchRepository _branchRepository;

        public BranchStaffService(
            IUserBranchRepository userBranchRepository,
            IUserRepository userRepository,
            IBranchRepository branchRepository)
        {
            _userBranchRepository = userBranchRepository;
            _userRepository = userRepository;
            _branchRepository = branchRepository;
        }

        public async Task<PagedResult<BranchStaffDto>> GetStaffAsync(Guid branchId, StaffFilterQuery query)
        {
            // Validate chi nhánh tồn tại
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Lấy danh sách staff từ repository (đã bao gồm filter và phân trang)
            var pagedResult = await _userBranchRepository.GetStaffByBranchAsync(branchId, query);

            // Map sang DTO
            var items = pagedResult.Items.Select(ub => new BranchStaffDto
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
                AssignedByName = null // Sẽ được bổ sung khi thêm audit fields
            }).ToList();

            return new PagedResult<BranchStaffDto>
            {
                Items = items,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize,
                TotalItems = pagedResult.TotalItems
            };
        }

        public async Task<BranchStaffDto> AddStaffAsync(Guid branchId, AddStaffDto dto, Guid currentUserId)
        {
            // Validate chi nhánh tồn tại
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Validate user tồn tại và đủ điều kiện
            var user = await _userRepository.GetUserByIdAsync(dto.UserId);
            if (user == null)
            {
                throw new AppException(404, "Người dùng không tồn tại", ErrorCodes.UserNotFound);
            }

            if (user.Status != UserStatus.ACTIVE)
            {
                throw new AppException(400, "Người dùng không ở trạng thái hoạt động", ErrorCodes.InvalidStaffUser);
            }

            // OWNER không được gán làm nhân viên
            if (user.Role == UserRole.OWNER)
            {
                throw new AppException(400, "Không thể gán OWNER làm nhân viên", ErrorCodes.InvalidStaffUser);
            }

            // Kiểm tra user đã được gán vào chi nhánh này chưa
            var existingAssignment = await _userBranchRepository.GetActiveAssignmentAsync(dto.UserId, branchId);

            if (existingAssignment != null)
            {
                throw new AppException(409, "Người dùng đã được gán vào chi nhánh này", ErrorCodes.StaffAlreadyExists);
            }

            // Tạo staff assignment mới
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

            // Cập nhật role toàn cục của user lên STAFF nếu hiện tại là CUSTOMER
            if (user.Role == UserRole.CUSTOMER)
            {
                user.Role = UserRole.STAFF;
                await _userRepository.UpdateUserAsync(user);
            }

            // Lấy thông tin người assign (nếu có)
            var assignedByUser = await _userRepository.GetUserByIdAsync(currentUserId);

            // Trả về thông tin staff mới
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
                AssignedByName = assignedByUser?.FullName
            };
        }

        public async Task RemoveStaffAsync(Guid branchId, Guid userId, RemoveStaffDto dto, Guid currentUserId)
        {
            // Validate chi nhánh tồn tại
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Lấy staff assignment hiện tại
            var staffAssignment = await _userBranchRepository.GetStaffAssignmentAsync(userId, branchId);

            if (staffAssignment == null)
            {
                throw new AppException(404, "Nhân viên không được tìm thấy trong chi nhánh này", ErrorCodes.StaffNotFound);
            }

            // Vô hiệu hóa staff assignment
            staffAssignment.IsActive = false;
            staffAssignment.EndedAt = DateTime.UtcNow;
            await _userBranchRepository.UpdateAsync(staffAssignment);

            // Cập nhật role của user nếu không còn assignment active nào khác
            var staffUser = await _userRepository.GetUserByIdAsync(userId);
            if (staffUser != null)
            {
                var hasOtherAssignments = await _userBranchRepository.HasOtherActiveAssignmentsAsync(userId, staffAssignment.Id);

                if (!hasOtherAssignments)
                {
                    staffUser.Role = UserRole.CUSTOMER;
                    await _userRepository.UpdateUserAsync(staffUser);
                }
            }
        }

        public async Task<BulkStaffOperationResultDto> BulkStaffOperationAsync(Guid branchId, BulkStaffOperationDto dto, Guid currentUserId)
        {
            // Validate chi nhánh tồn tại
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
                    
                    // Lấy tên user để báo lỗi
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
            // Lấy staff assignment hiện tại
            var staffAssignment = await _userBranchRepository.GetActiveAssignmentAsync(userId, branchId);

            if (staffAssignment == null)
            {
                throw new AppException(404, "Nhân viên không được tìm thấy trong chi nhánh này", ErrorCodes.StaffNotFound);
            }

            // Cập nhật role
            staffAssignment.Role = newRole;
            await _userBranchRepository.UpdateAsync(staffAssignment);
        }
    }
}
