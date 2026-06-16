namespace SmashCourt_BE.DTOs.Branch
{
    public class BranchCourtTypeDto
    {
        public Guid? Id { get; set; }
        public Guid CourtTypeId { get; set; }
        public string CourtTypeName { get; set; } = null!;
        public string? CourtTypeDescription { get; set; }
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int CourtCount { get; set; }
    }
}
