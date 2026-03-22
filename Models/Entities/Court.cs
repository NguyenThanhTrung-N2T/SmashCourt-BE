using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Court
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid CourtTypeId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? AvatarUrl { get; set; }
        public CourtStatus Status { get; set; } = CourtStatus.AVAILABLE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Branch Branch { get; set; } = null!;
        public CourtType CourtType { get; set; } = null!;
        public ICollection<BookingCourt> BookingCourts { get; set; } = [];
        public ICollection<SlotLock> SlotLocks { get; set; } = [];
    }
}
