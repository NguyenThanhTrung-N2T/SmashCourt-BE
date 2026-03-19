using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class CourtType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public CourtTypeStatus Status { get; set; } = CourtTypeStatus.ACTIVE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<BranchCourtType> BranchCourtTypes { get; set; } = [];
        public ICollection<Court> Courts { get; set; } = [];
        public ICollection<SystemPrice> SystemPrices { get; set; } = [];
        public ICollection<BranchPriceOverride> BranchPriceOverrides { get; set; } = [];
    }
}
