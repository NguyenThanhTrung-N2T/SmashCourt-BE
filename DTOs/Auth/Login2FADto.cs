using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Auth
{
    public class Login2FADto
    {
        [Required(ErrorMessage = "Temp token không được để trống")]
        public string TempToken { get; set; } = null!;

        [Required(ErrorMessage = "OTP không được để trống")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP phải đúng 6 số")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP chỉ gồm 6 chữ số")]
        public string OtpCode { get; set; } = null!;
    }
}
