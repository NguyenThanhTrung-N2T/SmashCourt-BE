using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Invoice
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public decimal CourtFee { get; set; }
        public decimal ServiceFee { get; set; } = 0;
        public decimal LoyaltyDiscountAmount { get; set; } = 0;
        public decimal PromotionDiscountAmount { get; set; } = 0;
        public decimal FinalTotal { get; set; }
        public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.UNPAID;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public ICollection<Payment> Payments { get; set; } = [];
    }
}
