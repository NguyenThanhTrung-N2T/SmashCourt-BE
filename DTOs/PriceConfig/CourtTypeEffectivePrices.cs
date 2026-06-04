namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Groups effective pricing slots by court type.
    /// Used in the public pricing API response.
    /// </summary>
    public class CourtTypeEffectivePrices
    {
        public Guid CourtTypeId { get; set; }
        public string CourtTypeName { get; set; } = null!;
        public List<EffectiveSlot> Slots { get; set; } = new();
    }
}
