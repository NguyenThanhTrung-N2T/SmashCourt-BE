namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingDashboardSummaryQuery
    {
        public Guid? BranchId { get; set; }
    }

    public class BookingDashboardSummaryDto
    {
        public int TodayBookings { get; set; }
        public int ActiveBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int CancelledBookings { get; set; }
        public decimal TodayRevenue { get; set; }
        public int PendingRefunds { get; set; }
    }
}
