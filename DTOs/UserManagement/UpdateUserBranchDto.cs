using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO để cập nhật chi nhánh của user
/// </summary>
public class UpdateUserBranchDto
{
    [Required(ErrorMessage = "Chi nhánh mới là bắt buộc")]
    public Guid NewBranchId { get; set; }
}
