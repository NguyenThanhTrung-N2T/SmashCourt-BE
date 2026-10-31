using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Invoice
    {
        public Guid Id { get; set; }
        public string InvoiceCode { get; set; } = null!;
        public Guid BookingId { get; set; }
        public decimal CourtFee { get; set; }
        public decimal ServiceFee { get; set; } = 0;
        public decimal LoyaltyDiscountAmount { get; set; } = 0;
        public decimal PromotionDiscountAmount { get; set; } = 0;
        public decimal FinalTotal { get; set; }
        public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.UNPAID;
        public PaymentTiming PaymentTiming { get; set; } = PaymentTiming.POSTPAID;  // Default: trả sau
        public DateTime? ExpiresAt { get; set; }

        // Cọc (NULL = đơn không áp dụng cọc)
        public decimal? DepositRate { get; set; }
        public decimal? DepositAmount { get; set; }
        public DepositSource? DepositSource { get; set; }
        public DateTime? HoldDeadline { get; set; }                 // Hạn giữ chỗ tuyệt đối; NULL hiểu là CreatedAt + PaymentHoldMaxMinutes
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public ICollection<Payment> Payments { get; set; } = [];
    }
}
