namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceVersionRowDto
    {
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public decimal WeekdayPrice { get; set; }
        public decimal WeekendPrice { get; set; }
    }
}
