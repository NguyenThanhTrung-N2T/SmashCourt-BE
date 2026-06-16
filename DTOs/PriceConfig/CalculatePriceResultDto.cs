namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CalculatePriceResultDto
    {
        /// <summary>Tổng phí sân cho tất cả các sân được chọn.</summary>
        public decimal TotalFee { get; set; }

        /// <summary>Chi tiết giá cho từng sân.</summary>
        public List<CourtPriceResultDto> Courts { get; set; } = [];
    }

    public class CourtPriceResultDto
    {
        public Guid CourtId { get; set; }
        public string CourtName { get; set; } = null!;
        public decimal CourtFee { get; set; }
        public List<PriceBreakdownDto> Breakdown { get; set; } = [];
    }
}
