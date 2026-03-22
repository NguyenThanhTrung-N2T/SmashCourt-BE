namespace SmashCourt_BE.Models.Entities
{
    public class CustomerLoyalty
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid TierId { get; set; }
        public int TotalPoints { get; set; } = 0;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
        public LoyaltyTier Tier { get; set; } = null!;
    }
}
