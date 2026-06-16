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

        // Session metadata - for multi-device session management
        public string? DeviceName { get; set; }      // "Chrome on Windows", "Safari on iPhone"
        public string? IpAddress { get; set; }       // IPv4 or IPv6
        public string? UserAgent { get; set; }       // Raw user agent (max 500 chars)
        public DateTime? LastUsedAt { get; set; }    // Updated on every token refresh

        // Navigation
        public User User { get; set; } = null!;
        public RefreshToken? RotatedFrom { get; set; }
    }
}
