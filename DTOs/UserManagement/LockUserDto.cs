using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO để khóa user
/// </summary>
public class LockUserDto
{
    [Required(ErrorMessage = "Lý do khóa là bắt buộc")]
    [MaxLength(500, ErrorMessage = "Lý do khóa không được vượt quá 500 ký tự")]
    public string Reason { get; set; } = null!;
}
