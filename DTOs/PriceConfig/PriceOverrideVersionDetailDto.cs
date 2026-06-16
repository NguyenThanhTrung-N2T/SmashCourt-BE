namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Response DTO for GET /api/prices/overrides/{effectiveFrom} endpoint.
    /// Returns the exact override version configuration for a specific effective date.
    /// This shows what was configured in the version, NOT the fully resolved pricing snapshot.
    /// </summary>
    public class PriceOverrideVersionDetailDto
    {
        /// <summary>
        /// Branch ID
        /// </summary>
        public Guid BranchId { get; set; }

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

    /// <summary>
    /// Individual price slot detail with weekday and weekend prices
    /// </summary>
    public class PriceSlotDetail
    {
        /// <summary>
        /// Start time in HH:mm:ss format
        /// </summary>
        public string StartTime { get; set; } = string.Empty;

        /// <summary>
        /// End time in HH:mm:ss format
        /// </summary>
        public string EndTime { get; set; } = string.Empty;

        /// <summary>
        /// Price for weekdays
        /// </summary>
        public decimal WeekdayPrice { get; set; }

        /// <summary>
        /// Price for weekends
        /// </summary>
        public decimal WeekendPrice { get; set; }
    }
}
