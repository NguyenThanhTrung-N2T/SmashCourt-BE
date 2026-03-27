using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.CourtType
{
    public class CourtTypeDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public CourtTypeStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int ActiveBranchCount { get; set; }
        public int CourtCount { get; set; }
    }
}
