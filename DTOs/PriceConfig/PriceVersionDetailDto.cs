namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceVersionDetailDto
    {
        public Guid CourtTypeId { get; set; }
        public string EffectiveFrom { get; set; } = string.Empty;
        public List<PriceVersionRowDto> Rows { get; set; } = new();
    }
}
