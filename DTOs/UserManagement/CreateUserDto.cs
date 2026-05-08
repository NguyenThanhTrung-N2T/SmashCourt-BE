using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO để tạo user mới (STAFF hoặc BRANCH_MANAGER)
/// </summary>
public class CreateUserDto
{
    [Required(ErrorMessage = "Email là bắt buộc")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Họ tên là bắt buộc")]
    [MaxLength(255, ErrorMessage = "Họ tên không được vượt quá 255 ký tự")]
    public string FullName { get; set; } = null!;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    [MaxLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự")]
    public string? Phone { get; set; }

    /// <summary>
    /// Role được yêu cầu (STAFF hoặc BRANCH_MANAGER)
    /// QUAN TRỌNG: Backend sẽ FORCE role dựa trên currentUserRole, KHÔNG trust input này
    /// </summary>
    [Required(ErrorMessage = "Role là bắt buộc")]
    public UserRole RequestedRole { get; set; }

    /// <summary>
    /// Chi nhánh được gán
    /// OWNER: bắt buộc
    /// BRANCH_MANAGER: tùy chọn (sẽ tự động sử dụng chi nhánh của manager nếu không cung cấp)
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Mật khẩu tạm thời (tùy chọn)
    /// Nếu không cung cấp, hệ thống sẽ tự động tạo
    /// </summary>
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    public string? TemporaryPassword { get; set; }
}
