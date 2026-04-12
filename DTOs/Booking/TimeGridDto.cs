namespace SmashCourt_BE.DTOs.Booking
{
    public class TimeGridSlotDto
    {
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } = null!; // AVAILABLE/LOCKED/BOOKED/IN_USE
        public int? LockRemainingSeconds { get; set; } // Nếu LOCKED
    }
}
