using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Auth;

/// <summary>
/// DTO cho việc đổi mật khẩu bắt buộc sau khi admin tạo user hoặc reset password
/// Dùng cho POST /auth/change-password (force change với temp token)
/// </summary>
public class ChangePasswordDto
{
    /// <summary>
    /// Temp token nhận được từ login response khi MustChangePassword = true
    /// </summary>
    [Required(ErrorMessage = "TempToken là bắt buộc")]
    public string TempToken { get; set; } = null!;

    /// <summary>
    /// Mật khẩu mới (tối thiểu 8 ký tự)
    /// </summary>
    [Required(ErrorMessage = "Mật khẩu mới là bắt buộc")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    public string NewPassword { get; set; } = null!;
}
