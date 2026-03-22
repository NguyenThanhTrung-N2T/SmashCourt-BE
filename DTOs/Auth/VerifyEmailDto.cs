using System.ComponentModel.DataAnnotations;

public class VerifyEmailDto
{
    [Required(ErrorMessage = "Email không được để trống")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "OTP không được để trống")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP phải đúng 6 số")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "OTP chỉ gồm 6 chữ số")]
    public string OtpCode { get; set; } = null!;
}