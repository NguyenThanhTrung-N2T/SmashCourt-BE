namespace SmashCourt_BE.DTOs.Booking
{
    public class CancelTokenInfoDto
    {
        public Guid BookingId { get; set; }
        public string BranchName { get; set; } = null!;
        /// <summary>Tên tất cả sân trong booking (hỗ trợ multi-court)</summary>
        public List<string> CourtNames { get; set; } = [];
        public DateTime BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal RefundPercent { get; set; }
        public string Status { get; set; } = null!;
    }
}
