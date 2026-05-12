using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Profile;

/// <summary>
/// DTO cho PUT /api/me/password - Đổi mật khẩu (authenticated user tự đổi)
/// </summary>
public class SelfChangePasswordDto
{
    [Required(ErrorMessage = "Mật khẩu hiện tại không được để trống")]
    public string CurrentPassword { get; set; } = null!;

    [Required(ErrorMessage = "Mật khẩu mới không được để trống")]
    [MinLength(8, ErrorMessage = "Mật khẩu mới phải có ít nhất 8 ký tự")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ thường, số và ký tự đặc biệt")]
    public string NewPassword { get; set; } = null!;

    [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống")]
    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp")]
    public string ConfirmPassword { get; set; } = null!;
}
