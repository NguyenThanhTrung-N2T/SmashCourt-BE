using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Promotion
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Code { get; set; }
        public string? PromoDisplayUrl { get; set; }
        public string? Description { get; set; }
        public DiscountTypeEnum DiscountType { get; set; } = DiscountTypeEnum.PERCENT;
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public int? UsageLimit { get; set; }
        public int? UsagePerUserLimit { get; set; }
        public int UsedCount { get; set; } = 0;
        public DateOnly StartDate { get; set; }
        public DateOnly EndDate { get; set; }
        public PromotionStatus Status { get; set; } = PromotionStatus.INACTIVE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<PromotionCondition> Conditions { get; set; } = [];
        public ICollection<BookingPromotion> BookingPromotions { get; set; } = [];
    }
}
