namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class TimeSlotDto
    {
        public Guid WeekdaySlotId { get; set; }
        public Guid WeekendSlotId { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }
}
