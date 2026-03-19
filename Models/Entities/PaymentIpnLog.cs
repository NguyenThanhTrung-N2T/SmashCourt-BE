using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class PaymentIpnLog
    {
        public Guid Id { get; set; }
        public Guid? PaymentId { get; set; }
        public IpnProvider Provider { get; set; }
        public string? ProviderTransactionId { get; set; }
        public string RawPayload { get; set; } = null!;
        public bool IsValid { get; set; }
        public DateTime ProcessedAt { get; set; }

        // Navigation
        public Payment? Payment { get; set; }
    }
}
