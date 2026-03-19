using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class TimeSlot
    {
        public Guid Id { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public DayType DayType { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public ICollection<SystemPrice> SystemPrices { get; set; } = [];
        public ICollection<BranchPriceOverride> BranchPriceOverrides { get; set; } = [];
        public ICollection<BookingPriceItem> BookingPriceItems { get; set; } = [];
    }
}
