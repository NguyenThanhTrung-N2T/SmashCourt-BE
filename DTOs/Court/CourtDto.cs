using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Court
{
    public class CourtDto
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid CourtTypeId { get; set; }
        public string CourtTypeName { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? AvatarUrl { get; set; }
        public CourtStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
