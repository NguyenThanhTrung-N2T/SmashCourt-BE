namespace SmashCourt_BE.Models.Entities
{
    public class SlotLock
    {
        public Guid Id { get; set; }
        public Guid CourtId { get; set; }
        public Guid BookingId { get; set; }
        public string? SessionId { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Court Court { get; set; } = null!;
        public Booking Booking { get; set; } = null!;
    }
}
