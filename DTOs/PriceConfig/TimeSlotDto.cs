namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class TimeSlotDto
    {
        public Guid WeekdaySlotId { get; set; }
        public Guid WeekendSlotId { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
    }
}
