using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Court
{
    /// <summary>
    /// Response for the management-timeline endpoint — full enriched slot data per court for one date.
    /// </summary>
    public class CourtManagementTimelineDto
    {
        public DateOnly Date { get; set; }
        public OperatingHoursDto OperatingHours { get; set; } = null!;
        public List<CourtTimelineRowDto> Courts { get; set; } = [];
    }

    public class OperatingHoursDto
    {
        /// <summary>HH:mm</summary>
        public string Open { get; set; } = null!;
        /// <summary>HH:mm</summary>
        public string Close { get; set; } = null!;
    }

    public class CourtTimelineRowDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string TypeName { get; set; } = null!;
        public CourtOperationalStatus OperationalStatus { get; set; }
        public List<CourtTimelineSlotDetailDto> Slots { get; set; } = [];
    }

    /// <summary>
    /// A merged time-segment on the full-detail timeline — carries booking identity so named blocks are possible.
    /// </summary>
    public class CourtTimelineSlotDetailDto
    {
        public string StartTime { get; set; } = null!;
        public string EndTime { get; set; } = null!;
        public CourtTimelineSlotStatus Status { get; set; }
        public Guid? BookingId { get; set; }
        public string? PlayerName { get; set; }
        public string? BookingStatus { get; set; }
        public string? ActualEndTime { get; set; }    // non-null = early checkout
        public bool IsEarlyCheckout { get; set; }
    }
}
