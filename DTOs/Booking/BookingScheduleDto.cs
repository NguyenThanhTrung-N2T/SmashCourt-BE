namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingScheduleQuery
    {
        public Guid? BranchId { get; set; }
        public DateTime Date { get; set; }
    }

    public class BookingScheduleCourtDto
    {
        public Guid CourtId { get; set; }
        public string CourtName { get; set; } = null!;
        public List<BookingScheduleItemDto> Bookings { get; set; } = [];
    }

    public class BookingScheduleItemDto
    {
        public Guid BookingId { get; set; }
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
