using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class UserBranchSummaryDto
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = null!;
        public UserBranchRole Role { get; set; }
        public bool IsActive { get; set; }
    }
}
