using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class BookingCalendarHeatmapQuery
    {
        [Range(1, 9999)]
        public int Year { get; set; }

        [Range(1, 12)]
        public int Month { get; set; }

        public Guid? BranchId { get; set; }
    }

    public class BookingCalendarHeatmapDto
    {
        public string Date { get; set; } = null!;
        public int BookingCount { get; set; }
        public decimal OccupancyRate { get; set; }
        public decimal Revenue { get; set; }
    }
}
