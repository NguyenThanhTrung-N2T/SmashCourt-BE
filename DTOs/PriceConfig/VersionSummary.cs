namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Summary of a single price override version with its status.
    /// </summary>
    public class VersionSummary
    {
        public string EffectiveFrom { get; set; } = null!; // yyyy-MM-dd format
        public string Status { get; set; } = null!; // "ACTIVE", "SCHEDULED", "EXPIRED"
    }
}
