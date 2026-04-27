using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class UserSearchResultDto
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public UserRole CurrentRole { get; set; }
        public UserStatus Status { get; set; }
        public List<UserBranchSummaryDto> CurrentAssignments { get; set; } = [];
        public bool IsEligibleForManager { get; set; }
        public bool IsEligibleForStaff { get; set; }
    }

    public class UserBranchSummaryDto
    {
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = null!;
        public UserBranchRole Role { get; set; }
        public bool IsActive { get; set; }
    }
}