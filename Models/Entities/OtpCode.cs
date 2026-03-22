using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class OtpCode
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public OtpType Type { get; set; }
        public string CodeHash { get; set; } = null!;
        public int AttemptCount { get; set; } = 0;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
    }
}
