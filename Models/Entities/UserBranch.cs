using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class UserBranch
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid BranchId { get; set; }
        public UserBranchRole Role { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime AssignedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
        public Branch Branch { get; set; } = null!;
    }
}
