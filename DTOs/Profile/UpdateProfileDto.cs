using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Profile;

/// <summary>
/// DTO cho PUT /api/me - Cập nhật profile
/// </summary>
public class UpdateProfileDto
{
    [Required(ErrorMessage = "Tên không được để trống")]
    [StringLength(100, ErrorMessage = "Tên không được vượt quá 100 ký tự")]
    public string FullName { get; set; } = null!;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự")]
    public string? Phone { get; set; }

    [Url(ErrorMessage = "URL avatar không hợp lệ")]
    [StringLength(1000, ErrorMessage = "URL avatar không được vượt quá 1000 ký tự")]
    public string? AvatarUrl { get; set; }
}
