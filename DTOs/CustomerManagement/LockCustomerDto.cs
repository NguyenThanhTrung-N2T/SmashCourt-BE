using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// DTO cho khóa tài khoản khách hàng
/// </summary>
public class LockCustomerDto
{
    [Required(ErrorMessage = "Lý do khóa là bắt buộc")]
    [StringLength(500, ErrorMessage = "Lý do khóa không được vượt quá 500 ký tự")]
    public string Reason { get; set; } = null!;
}
