namespace SmashCourt_BE.DTOs.Loyalty
{
    public class LoyaltyTransactionDto
    {
        public Guid Id { get; set; }
        public Guid? BookingId { get; set; }
        public int Points { get; set; }
        public int TotalPointsAfter { get; set; }
        public string Type { get; set; } = null!;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
