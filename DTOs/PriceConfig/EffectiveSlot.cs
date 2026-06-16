namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Represents a single effective time slot with pricing information.
    /// Used in the public pricing API response.
    /// </summary>
    public class EffectiveSlot
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public decimal WeekdayPrice { get; set; }
        public decimal WeekendPrice { get; set; }
        public string EffectiveFrom { get; set; } = null!;
        public string PriceSource { get; set; } = null!; // "BRANCH_OVERRIDE" or "SYSTEM_PRICE"
    }
}
