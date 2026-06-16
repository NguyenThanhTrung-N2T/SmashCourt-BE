namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Response DTO for GET /api/system-prices/versions/{effectiveFrom} endpoint.
    /// Returns the exact system price version configuration for a specific effective date.
    /// This shows what was configured in the version, NOT a fully resolved pricing snapshot.
    /// </summary>
    public class SystemPriceVersionDetailDto
    {
        /// <summary>
        /// Court type ID
        /// </summary>
        public Guid CourtTypeId { get; set; }

        /// <summary>
        /// Court type name
        /// </summary>
        public string CourtTypeName { get; set; } = string.Empty;

        /// <summary>
        /// Effective date in yyyy-MM-dd format
        /// </summary>
        public string EffectiveFrom { get; set; } = string.Empty;

        /// <summary>
        /// Version status: ACTIVE, SCHEDULED, or EXPIRED
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// List of price slots for this version
        /// </summary>
        public List<PriceSlotDetail> Slots { get; set; } = new();
    }
}
