using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Promotion
{
    /// <summary>
    /// Response DTO cho promotion áp dụng được, bao gồm thông tin discount đã tính
    /// </summary>
    public class ApplicablePromotionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Code { get; set; }
        public string? PromoDisplayUrl { get; set; }
        public string? Description { get; set; }
        public DiscountTypeEnum DiscountType { get; set; }
        public decimal DiscountValue { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        
        // Calculated fields
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
        
        // Usage info
        public int? UsageLimit { get; set; }
        public int UsedCount { get; set; }
        public int? RemainingUsage { get; set; }
        public int? UsagePerUserLimit { get; set; }
        public int UserUsageCount { get; set; }
        public int? RemainingUserUsage { get; set; }
    }
}
