using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class RemoveManagerDto
    {
        [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự")]
        public string? Reason { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
        public string? Notes { get; set; }
    }
}