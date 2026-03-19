namespace SmashCourt_BE.Models.Entities
{
    public class LoyaltyTier
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int MinPoints { get; set; }
        public decimal DiscountRate { get; set; } = 0;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<CustomerLoyalty> CustomerLoyalties { get; set; } = [];
    }
}
