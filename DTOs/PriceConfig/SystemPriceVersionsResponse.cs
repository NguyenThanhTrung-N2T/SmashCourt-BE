namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Response DTO for GET /api/system-prices/versions endpoint.
    /// Returns all version dates for a court type with their statuses.
    /// </summary>
    public class SystemPriceVersionsResponse
    {
        /// <summary>
        /// Court type ID
        /// </summary>
        public Guid CourtTypeId { get; set; }

        /// <summary>
        /// List of price versions
        /// </summary>
        public List<VersionSummary> Versions { get; set; } = new();
    }
}
