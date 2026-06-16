namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Response DTO for GET /api/prices endpoint.
    /// Returns effective pricing snapshot for a branch on a specific date.
    /// </summary>
    public class EffectivePricesResponse
    {
        public Guid BranchId { get; set; }
        public string Date { get; set; } = null!; // yyyy-MM-dd format
        public List<CourtTypeEffectivePrices> CourtTypes { get; set; } = new();
    }
}
