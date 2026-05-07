using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO để cập nhật thông tin user
/// </summary>
public class UpdateUserDto
{
    [Required(ErrorMessage = "Họ tên là bắt buộc")]
    [MaxLength(255, ErrorMessage = "Họ tên không được vượt quá 255 ký tự")]
    public string FullName { get; set; } = null!;

    [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
    [MaxLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự")]
    public string? Phone { get; set; }

    public string? AvatarUrl { get; set; }
}
