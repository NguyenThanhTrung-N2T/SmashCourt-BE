namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Response DTO for GET /api/prices/overrides endpoint.
    /// Returns all pricing override versions for a court type with their status.
    /// </summary>
    public class PriceOverrideVersionsResponse
    {
        public Guid BranchId { get; set; }
        public Guid CourtTypeId { get; set; }
        public List<VersionSummary> Versions { get; set; } = new();
    }
}
