using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.UserManagement;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Service quản lý users (STAFF, BRANCH_MANAGER) - chỉ dành cho OWNER và BRANCH_MANAGER
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Lấy danh sách users với filter và phân trang
    /// OWNER: Xem tất cả users
    /// BRANCH_MANAGER: Chỉ xem STAFF trong chi nhánh của mình
    /// </summary>
    Task<PagedResult<UserDto>> GetUsersAsync(UserListQuery query, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Lấy thông tin chi tiết user
    /// OWNER: Xem bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ xem STAFF trong chi nhánh của mình
    /// </summary>
    Task<UserDetailDto> GetUserByIdAsync(Guid userId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Tạo user mới (STAFF hoặc BRANCH_MANAGER)
    /// OWNER: Có thể tạo STAFF hoặc BRANCH_MANAGER
    /// BRANCH_MANAGER: Chỉ có thể tạo STAFF trong chi nhánh của mình
    /// </summary>
    Task<UserDetailDto> CreateUserAsync(CreateUserDto dto, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Cập nhật thông tin user
    /// OWNER: Có thể cập nhật bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể cập nhật STAFF trong chi nhánh của mình
    /// </summary>
    Task<UserDetailDto> UpdateUserAsync(Guid userId, UpdateUserDto dto, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Khóa user (status = LOCKED)
    /// OWNER: Có thể khóa bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể khóa STAFF trong chi nhánh của mình
    /// </summary>
    Task LockUserAsync(Guid userId, LockUserDto dto, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Mở khóa user (status = ACTIVE)
    /// OWNER: Có thể mở khóa bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể mở khóa STAFF trong chi nhánh của mình
    /// </summary>
    Task UnlockUserAsync(Guid userId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Đánh dấu user là INACTIVE (nghỉ việc, archived)
    /// OWNER: Có thể inactive bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể inactive STAFF trong chi nhánh của mình
    /// </summary>
    Task DeactivateUserAsync(Guid userId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Kích hoạt lại user từ INACTIVE về ACTIVE
    /// OWNER: Có thể activate bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể activate STAFF trong chi nhánh của mình
    /// </summary>
    Task ActivateUserAsync(Guid userId, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Cập nhật chi nhánh của user
    /// OWNER: Có thể chuyển bất kỳ user nào sang chi nhánh khác
    /// BRANCH_MANAGER: Không có quyền (403)
    /// </summary>
    Task UpdateUserBranchAsync(Guid userId, UpdateUserBranchDto dto, Guid currentUserId, string currentUserRole);

    /// <summary>
    /// Reset password cho user
    /// OWNER: Có thể reset password cho bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể reset password cho STAFF trong chi nhánh của mình
    /// Trả về: message thông báo thành công (KHÔNG trả về password qua API)
    /// Password sẽ được gửi qua email
    /// </summary>
    Task<string> ResetPasswordAsync(Guid userId, ResetPasswordDto dto, Guid currentUserId, string currentUserRole);
}
