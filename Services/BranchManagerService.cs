using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchManagerService : IBranchManagerService
    {
        private readonly IUserBranchRepository _userBranchRepository;
        private readonly IUserRepository _userRepository;
        private readonly IBranchRepository _branchRepository;

        public BranchManagerService(
            IUserBranchRepository userBranchRepository,
            IUserRepository userRepository,
            IBranchRepository branchRepository)
        {
            _userBranchRepository = userBranchRepository;
            _userRepository = userRepository;
            _branchRepository = branchRepository;
        }

        public async Task<BranchManagerDto?> GetCurrentManagerAsync(Guid branchId)
        {
            // Validate chi nhánh tồn tại
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Lấy manager active hiện tại (bao gồm User navigation)
            var managerAssignment = await _userBranchRepository.GetActiveManagerWithUserAsync(branchId);

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
                AssignedByName = null,
                AssignedByUserId = null
            };
        }

        public async Task<BranchManagerDto> AssignManagerAsync(Guid branchId, AssignManagerDto dto, Guid currentUserId)
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
                throw new AppException(400, "Người dùng không ở trạng thái hoạt động", ErrorCodes.InvalidManagerUser);
            }

            // Chỉ user có role BRANCH_MANAGER mới có thể được gán làm quản lý chi nhánh
            if (user.Role != UserRole.BRANCH_MANAGER)
            {
                throw new AppException(400, "Chỉ người dùng có vai trò BRANCH_MANAGER mới có thể được gán làm quản lý chi nhánh", ErrorCodes.InvalidManagerUser);
            }

            // Kiểm tra user đã là manager của chi nhánh khác chưa
            var existingManagerAssignment = await _userBranchRepository.GetActiveManagerAssignmentByUserIdAsync(dto.UserId);

            if (existingManagerAssignment != null)
            {
                throw new AppException(409, "Người dùng đã là quản lý của chi nhánh khác", ErrorCodes.ManagerAlreadyExists);
            }

            // Xóa manager hiện tại nếu có
            var currentManager = await _userBranchRepository.GetActiveManagerByBranchIdAsync(branchId);
            if (currentManager != null)
            {
                currentManager.IsActive = false;
                currentManager.EndedAt = DateTime.UtcNow;
                await _userBranchRepository.UpdateAsync(currentManager);

                // KHÔNG tự động downgrade role của manager cũ
                // Role BRANCH_MANAGER là do OWNER cấp, không tự động thay đổi
                // Manager cũ vẫn giữ role BRANCH_MANAGER để có thể được assign vào chi nhánh khác
            }

            // Tạo manager assignment mới
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

            // Lưu ý: User phải có sẵn role BRANCH_MANAGER để được assign
            // Chúng ta không thay đổi role của user ở đây

            // Trả về thông tin manager mới
            return new BranchManagerDto
            {
                UserId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl,
                AssignedAt = newManagerAssignment.AssignedAt,
                AssignedByName = null,
                AssignedByUserId = currentUserId
            };
        }

        public async Task RemoveManagerAsync(Guid branchId, RemoveManagerDto dto, Guid currentUserId)
        {
            // Validate chi nhánh tồn tại
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch == null)
            {
                throw new AppException(404, "Chi nhánh không tồn tại", ErrorCodes.BranchNotFound);
            }

            // Lấy manager hiện tại
            var currentManager = await _userBranchRepository.GetActiveManagerByBranchIdAsync(branchId);
            if (currentManager == null)
            {
                throw new AppException(404, "Chi nhánh hiện tại không có quản lý", ErrorCodes.ManagerNotFound);
            }

            // Vô hiệu hóa manager assignment
            currentManager.IsActive = false;
            currentManager.EndedAt = DateTime.UtcNow;
            await _userBranchRepository.UpdateAsync(currentManager);

            // KHÔNG tự động downgrade role của manager
            // Role BRANCH_MANAGER là do OWNER cấp, không tự động thay đổi
            // Manager vẫn giữ role BRANCH_MANAGER để có thể được assign vào chi nhánh khác
        }
    }
}
