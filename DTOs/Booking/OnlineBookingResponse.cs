namespace SmashCourt_BE.DTOs.Booking
{
    public class OnlineBookingResponse
    {
        public Guid BookingId { get; set; }
        public string PaymentUrl { get; set; } = null!; // VNPay URL
        public DateTime ExpiresAt { get; set; }
        public decimal FinalTotal { get; set; }
    }
}
