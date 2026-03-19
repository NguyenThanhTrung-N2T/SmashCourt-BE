namespace SmashCourt_BE.Models.Entities
{
    public class BranchCourtType
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid CourtTypeId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Navigation
        public Branch Branch { get; set; } = null!;
        public CourtType CourtType { get; set; } = null!;
    }
}
