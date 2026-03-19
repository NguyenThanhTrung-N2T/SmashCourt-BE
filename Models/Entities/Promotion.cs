using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Promotion
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal DiscountRate { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public PromotionStatus Status { get; set; } = PromotionStatus.INACTIVE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<BookingPromotion> BookingPromotions { get; set; } = [];
    }
}
