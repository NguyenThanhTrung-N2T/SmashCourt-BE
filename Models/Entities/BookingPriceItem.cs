namespace SmashCourt_BE.Models.Entities
{
    public class BookingPriceItem
    {
        public Guid Id { get; set; }
        public Guid BookingCourtId { get; set; }
        public Guid TimeSlotId { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public BookingCourt BookingCourt { get; set; } = null!;
        public TimeSlot TimeSlot { get; set; } = null!;
    }
}
