using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BulkStaffOperationDto
    {
        [Required(ErrorMessage = "Operation không được để trống")]
        public BulkOperationType Operation { get; set; }

        [Required(ErrorMessage = "UserIds không được để trống")]
        [MinLength(1, ErrorMessage = "Phải có ít nhất 1 user")]
        public List<Guid> UserIds { get; set; } = [];

        public UserBranchRole? NewRole { get; set; }

        [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự")]
        public string? Reason { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
        public string? Notes { get; set; }
    }
}
