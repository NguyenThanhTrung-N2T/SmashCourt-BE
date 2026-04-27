namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceBreakdownDto
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Hours { get; set; }
        public decimal SubTotal { get; set; }
        public string PriceSource { get; set; } = null!; // SYSTEM_PRICE | BRANCH_OVERRIDE
    }
}
