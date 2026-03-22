using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Booking
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid? CustomerId { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }
        public DateOnly BookingDate { get; set; }
        public BookingStatus Status { get; set; } = BookingStatus.PENDING;
        public BookingSource Source { get; set; }
        public string? Note { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public Guid? CancelledBy { get; set; }
        public DateTime? CancelledAt { get; set; }
        public CancelSourceEnum? CancelSource { get; set; }
        public string? CancelTokenHash { get; set; }
        public DateTime? CancelTokenExpiresAt { get; set; }
        public DateTime? CancelTokenUsedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Branch Branch { get; set; } = null!;
        public User? Customer { get; set; }
        public User? CreatedByUser { get; set; }
        public User? CancelledByUser { get; set; }
        public ICollection<BookingCourt> BookingCourts { get; set; } = [];
        public ICollection<BookingService> BookingServices { get; set; } = [];
        public BookingPromotion? BookingPromotion { get; set; }
        public SlotLock? SlotLock { get; set; }
        public Invoice? Invoice { get; set; }
        public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
    }
}
