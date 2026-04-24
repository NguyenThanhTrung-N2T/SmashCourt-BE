using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Promotion
{
    public class PromotionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public decimal DiscountRate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public PromotionStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
