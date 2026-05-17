using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.Promotions
{
    public class PromotionValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public Promotion? Promotion { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalAmount { get; set; }
    }
}