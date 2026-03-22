using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Entities
{
    public class Service
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Unit { get; set; } = null!;
        public decimal DefaultPrice { get; set; }
        public ServiceStatus Status { get; set; } = ServiceStatus.ACTIVE;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation
        public ICollection<BranchService> BranchServices { get; set; } = [];
        public ICollection<BookingService> BookingServices { get; set; } = [];
    }
}
