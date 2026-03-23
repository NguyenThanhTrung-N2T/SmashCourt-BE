namespace SmashCourt_BE.DTOs.LoyaltyTier
{
    public class LoyaltyTierDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int MinPoints { get; set; }
        public decimal DiscountRate { get; set; }
    }
}
