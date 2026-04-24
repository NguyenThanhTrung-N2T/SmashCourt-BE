namespace SmashCourt_BE.DTOs.PriceConfig
{

    public class CurrentPriceDto
    {
        public Guid CourtTypeId { get; set; }
        public string CourtTypeName { get; set; } = null!;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public decimal WeekdayPrice { get; set; }
        public decimal WeekendPrice { get; set; }
        public DateTime EffectiveFrom { get; set; }
    }
}
