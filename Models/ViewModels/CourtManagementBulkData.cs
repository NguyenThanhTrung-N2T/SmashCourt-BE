using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Raw data bundle from DB for the court management dashboard.
    /// </summary>
    public class CourtManagementBulkData
    {
        public Branch Branch { get; set; } = null!;
        public List<Court> Courts { get; set; } = [];
        public List<BookingCourt> TodayBookingCourts { get; set; } = [];
    }
}
