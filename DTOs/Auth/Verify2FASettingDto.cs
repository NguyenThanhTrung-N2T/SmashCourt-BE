using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Auth
{
    /// <summary>
    /// DTO dùng chung cho verify OTP khi bật/tắt 2FA
    /// </summary>
    public class Verify2FASettingDto
    {
        [Required(ErrorMessage = "Mã OTP không được để trống")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự")]
        public string OtpCode { get; set; } = string.Empty;
    }
}
