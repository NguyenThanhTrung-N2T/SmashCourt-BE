namespace SmashCourt_BE.Models.Entities
{
    public class BookingCourt
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid CourtId { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public Court Court { get; set; } = null!;
        public ICollection<BookingPriceItem> BookingPriceItems { get; set; } = [];
    }
}
