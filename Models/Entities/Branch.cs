using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Branch
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Address { get; set; } = null!;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public TimeOnly OpenTime { get; set; }
        public TimeOnly CloseTime { get; set; }
        public BranchStatus Status { get; set; } = BranchStatus.ACTIVE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<UserBranch> UserBranches { get; set; } = [];
        public ICollection<BranchCourtType> BranchCourtTypes { get; set; } = [];
        public ICollection<Court> Courts { get; set; } = [];
        public ICollection<BranchService> BranchServices { get; set; } = [];
        public ICollection<BranchPriceOverride> BranchPriceOverrides { get; set; } = [];
        public ICollection<Booking> Bookings { get; set; } = [];
    }
}
