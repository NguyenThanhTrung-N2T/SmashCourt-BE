using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Refund
    {
        public Guid Id { get; set; }
        public Guid PaymentId { get; set; }
        public decimal Amount { get; set; }
        public decimal RefundPercent { get; set; }
        public RefundStatus Status { get; set; } = RefundStatus.PENDING;
        public Guid? ProcessedBy { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Payment Payment { get; set; } = null!;
        public User? ProcessedByUser { get; set; }
    }
}
