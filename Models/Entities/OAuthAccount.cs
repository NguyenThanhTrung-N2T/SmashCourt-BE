namespace SmashCourt_BE.Models.Entities
{
    public class OAuthAccount
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Provider { get; set; } = null!;
        public string ProviderUserId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
    }
}
