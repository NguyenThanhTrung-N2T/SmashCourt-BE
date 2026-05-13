using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Promotion
{
    public class PromotionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Code { get; set; }
        public string? PromoDisplayUrl { get; set; }
        public string? Description { get; set; }
        public DiscountTypeEnum DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public int? UsageLimit { get; set; }
        public int? UsagePerUserLimit { get; set; }
        public int UsedCount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public PromotionStatus Status { get; set; }
        public List<PromotionConditionDto>? Conditions { get; set; }
    }
}
