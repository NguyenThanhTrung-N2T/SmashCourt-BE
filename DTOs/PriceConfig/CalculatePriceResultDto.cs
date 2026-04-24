namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CalculatePriceResultDto
    {
        public decimal CourtFee { get; set; }
        public List<PriceBreakdownDto> Breakdown { get; set; } = [];
    }

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
