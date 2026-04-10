namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class EffectivePriceDto
    {
        public Guid CourtTypeId { get; set; }
        public string CourtTypeName { get; set; } = null!;
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public decimal WeekdayPrice { get; set; }
        public decimal WeekendPrice { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public string PriceSource { get; set; } = null!;
    }
}
