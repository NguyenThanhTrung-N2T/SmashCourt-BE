namespace SmashCourt_BE.Models.Entities
{
    public class BookingPromotion
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid PromotionId { get; set; }
        public string PromotionNameSnapshot { get; set; } = null!;
        public decimal DiscountRateSnapshot { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public Promotion Promotion { get; set; } = null!;
    }
}
