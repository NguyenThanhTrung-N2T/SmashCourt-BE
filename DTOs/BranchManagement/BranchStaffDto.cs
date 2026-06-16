using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BranchStaffDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public UserBranchRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public string? AssignedByName { get; set; }
    }
}