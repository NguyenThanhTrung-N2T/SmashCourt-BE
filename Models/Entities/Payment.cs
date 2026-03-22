using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Payment
    {
        public Guid Id { get; set; }
        public Guid InvoiceId { get; set; }
        public PaymentTxMethod Method { get; set; }
        public decimal Amount { get; set; }
        public decimal RefundedAmount { get; set; } = 0;
        public PaymentTxStatus Status { get; set; } = PaymentTxStatus.PENDING;
        public string? TransactionRef { get; set; }
        public DateTime? PaidAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Invoice Invoice { get; set; } = null!;
        public ICollection<PaymentIpnLog> PaymentIpnLogs { get; set; } = [];
        public ICollection<Refund> Refunds { get; set; } = [];
    }
}
