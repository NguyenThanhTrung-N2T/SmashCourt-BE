using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BranchManagerDto
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime AssignedAt { get; set; }
        public string? AssignedByName { get; set; }
        public Guid? AssignedByUserId { get; set; }
    }
}