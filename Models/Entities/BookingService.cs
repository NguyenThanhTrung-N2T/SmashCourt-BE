namespace SmashCourt_BE.Models.Entities
{
    public class BookingService
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid ServiceId { get; set; }
        public string ServiceName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Booking Booking { get; set; } = null!;
        public Service Service { get; set; } = null!;
    }
}
