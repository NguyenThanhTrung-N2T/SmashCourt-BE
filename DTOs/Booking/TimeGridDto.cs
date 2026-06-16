namespace SmashCourt_BE.DTOs.Booking
{
    public class TimeGridSlotDto
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string Status { get; set; } = null!; // AVAILABLE/LOCKED/IN_USE
        public int? LockRemainingSeconds { get; set; } // Nếu LOCKED
    }
}
