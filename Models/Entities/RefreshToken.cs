namespace SmashCourt_BE.Models.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = null!;
        public Guid? RotatedFromId { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
        public RefreshToken? RotatedFrom { get; set; }
    }
}
