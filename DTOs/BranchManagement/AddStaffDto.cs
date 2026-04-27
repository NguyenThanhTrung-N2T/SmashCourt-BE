using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class AddStaffDto
    {
        [Required(ErrorMessage = "UserId không được để trống")]
        public Guid UserId { get; set; }

        public UserBranchRole Role { get; set; } = UserBranchRole.STAFF;

        [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự")]
        public string? Reason { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
        public string? Notes { get; set; }
    }
}