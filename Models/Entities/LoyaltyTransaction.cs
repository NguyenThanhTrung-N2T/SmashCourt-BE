using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class LoyaltyTransaction
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? BookingId { get; set; }
        public int Points { get; set; }
        public int TotalPointsAfter { get; set; }
        public LoyaltyTransactionType Type { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public User User { get; set; } = null!;
        public Booking? Booking { get; set; }
    }
}
