using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class BranchService
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid ServiceId { get; set; }
        public decimal Price { get; set; }
        public BranchServiceStatus Status { get; set; } = BranchServiceStatus.ENABLED;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public Branch Branch { get; set; } = null!;
        public Service Service { get; set; } = null!;
    }
}
