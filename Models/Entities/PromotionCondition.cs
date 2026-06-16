namespace SmashCourt_BE.Models.Entities
{
    public class PromotionCondition
    {
        public Guid Id { get; set; }
        public Guid PromotionId { get; set; }
        public string ConditionType { get; set; } = null!;
        public string ConditionValue { get; set; } = null!;

        // Navigation
        public Promotion Promotion { get; set; } = null!;
    }
}
