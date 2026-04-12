namespace SmashCourt_BE.DTOs.Booking
{
    public class CancelTokenInfoDto
    {
        public Guid BookingId { get; set; }
        public string BranchName { get; set; } = null!;
        public string CourtName { get; set; } = null!;
        public DateOnly BookingDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal RefundPercent { get; set; }
        public string Status { get; set; } = null!;
    }
}
