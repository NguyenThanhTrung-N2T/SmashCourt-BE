namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceVersionListDto
    {
        public string EffectiveFrom { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
    }
}
