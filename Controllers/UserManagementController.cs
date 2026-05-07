using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.UserManagement;
using SmashCourt_BE.Services.IService;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

/// <summary>
/// Controller quản lý users (STAFF, BRANCH_MANAGER)
/// Chỉ dành cho OWNER và BRANCH_MANAGER
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.OwnerOrManager)]
public class UserManagementController : ControllerBase
{
    private readonly IUserManagementService _service;

    public UserManagementController(IUserManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// Lấy danh sách users với filter và phân trang
    /// OWNER: Xem tất cả users
    /// BRANCH_MANAGER: Chỉ xem STAFF trong chi nhánh của mình
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers([FromQuery] UserListQuery query)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetUsersAsync(query, currentUserId, currentUserRole);
        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result, "Lấy danh sách người dùng thành công"));
    }

    /// <summary>
    /// Lấy thông tin chi tiết user
    /// OWNER: Xem bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ xem STAFF trong chi nhánh của mình
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.GetUserByIdAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Lấy thông tin người dùng thành công"));
    }

    /// <summary>
    /// Tạo user mới (STAFF hoặc BRANCH_MANAGER)
    /// OWNER: Có thể tạo STAFF hoặc BRANCH_MANAGER
    /// BRANCH_MANAGER: Chỉ có thể tạo STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.CreateUserAsync(dto, currentUserId, currentUserRole);
        return StatusCode(201, ApiResponse<UserDetailDto>.Ok(result, "Tạo người dùng thành công"));
    }

    /// <summary>
    /// Cập nhật thông tin user
    /// OWNER: Có thể cập nhật bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể cập nhật STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await _service.UpdateUserAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<UserDetailDto>.Ok(result, "Cập nhật người dùng thành công"));
    }

    /// <summary>
    /// Khóa user (status = LOCKED)
    /// OWNER: Có thể khóa bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể khóa STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LockUser(Guid id, [FromBody] LockUserDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.LockUserAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Khóa người dùng thành công"));
    }

    /// <summary>
    /// Mở khóa user (status = ACTIVE)
    /// OWNER: Có thể mở khóa bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể mở khóa STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUser(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.UnlockUserAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Mở khóa người dùng thành công"));
    }

    /// <summary>
    /// Đánh dấu user là INACTIVE (nghỉ việc, archived)
    /// OWNER: Có thể inactive bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể inactive STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.DeactivateUserAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Vô hiệu hóa người dùng thành công"));
    }

    /// <summary>
    /// Kích hoạt lại user từ INACTIVE về ACTIVE
    /// OWNER: Có thể activate bất kỳ user nào
    /// BRANCH_MANAGER: Chỉ có thể activate STAFF trong chi nhánh của mình
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ActivateUser(Guid id)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.ActivateUserAsync(id, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Kích hoạt người dùng thành công"));
    }

    /// <summary>
    /// Cập nhật chi nhánh của user
    /// OWNER: Có thể chuyển bất kỳ user nào sang chi nhánh khác
    /// BRANCH_MANAGER: Không có quyền (403)
    /// </summary>
    [HttpPut("{id:guid}/branch")]
    [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserBranch(Guid id, [FromBody] UpdateUserBranchDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        await _service.UpdateUserBranchAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<object>.Ok(null!, "Cập nhật chi nhánh thành công"));
    }

    /// <summary>
    /// Reset password cho user
    /// OWNER: Có thể reset password cho bất kỳ user nào (trừ chính mình)
    /// BRANCH_MANAGER: Chỉ có thể reset password cho STAFF trong chi nhánh của mình
    /// Trả về: message thông báo thành công (KHÔNG trả về password qua API)
    /// Password sẽ được gửi qua email
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto dto)
    {
        var currentUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var currentUserRole = User.FindFirstValue(ClaimTypes.Role)!;

        var message = await _service.ResetPasswordAsync(id, dto, currentUserId, currentUserRole);
        return Ok(ApiResponse<string>.Ok(message, message));
    }
}
