using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Branch
{
    public class BranchDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Address { get; set; } = null!;
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public TimeSpan OpenTime { get; set; }
        public TimeSpan CloseTime { get; set; }
        public BranchStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public string? ManagerName { get; set; }
        public Guid? ManagerId { get; set; }
    }
}
