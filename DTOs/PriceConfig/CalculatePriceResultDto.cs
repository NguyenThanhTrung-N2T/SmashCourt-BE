namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CalculatePriceResultDto
    {
        public decimal CourtFee { get; set; }
        public List<PriceBreakdownDto> Breakdown { get; set; } = [];
    }
}
