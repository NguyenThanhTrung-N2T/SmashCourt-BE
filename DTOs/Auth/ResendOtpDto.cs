using SmashCourt_BE.Models.Enums;
using System.ComponentModel.DataAnnotations;

public class ResendOtpDto
{
    [Required(ErrorMessage = "Email không được để trống")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "Loại OTP không được để trống")]
    public OtpType Type { get; set; }
}