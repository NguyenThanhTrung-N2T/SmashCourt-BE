using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    /// <summary>
    /// Request DTO for PATCH /api/system-prices/versions/{effectiveFrom} endpoint.
    /// Creates or partially updates a system price version.
    /// PATCH semantics: only submitted slots are touched — other slots in the version are left unchanged.
    /// Supports large time spans: a single slot input (e.g. 06:00–12:00) is automatically expanded
    /// into all constituent DB time slots.
    /// </summary>
    public class UpsertPriceRequest
    {
        /// <summary>
        /// List of price slots to create or update.
        /// Each slot input can span multiple DB time slots and will be expanded automatically.
        /// </summary>
        [Required]
        [MinLength(1, ErrorMessage = "Phải có ít nhất 1 cấu hình giá")]
        public List<PriceSlotInput> Slots { get; set; } = new();
    }
}
