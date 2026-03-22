using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Auth
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "Reset token không được để trống")]
        public string ResetToken { get; set; } = null!;

        [Required(ErrorMessage = "Mật khẩu không được để trống")]
        [RegularExpression(
            @"^(?=.*[A-Z])(?=.*[0-9])(?=.*[^a-zA-Z0-9]).{8,}$",
            ErrorMessage = "Mật khẩu tối thiểu 8 ký tự, có ít nhất 1 chữ hoa, 1 số, 1 ký tự đặc biệt")]
        public string NewPassword { get; set; } = null!;

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống")]
        [Compare("NewPassword", ErrorMessage = "Mật khẩu xác nhận không khớp")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
