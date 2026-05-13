namespace SmashCourt_BE.Services.Promotions
{
    public class PromotionContext
    {
        public Guid UserId { get; set; }
        public Guid BranchId { get; set; }
        public Guid CourtId { get; set; }
        public decimal BookingAmount { get; set; }
        public DateTime BookingDate { get; set; }
        public string Sport { get; set; } = null!;
        public int PreviousBookingCount { get; set; }
    }
}
