using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO để reset password cho user
/// </summary>
public class ResetPasswordDto
{
    /// <summary>
    /// Mật khẩu mới (tùy chọn)
    /// Nếu không cung cấp, hệ thống sẽ tự động tạo
    /// </summary>
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    public string? NewPassword { get; set; }
}
