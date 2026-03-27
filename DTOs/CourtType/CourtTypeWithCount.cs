namespace SmashCourt_BE.DTOs.CourtType
{
    // // Internal model — dùng trong repo, không expose ra ngoài
    public class CourtTypeWithCount
    {
        public Models.Entities.CourtType CourtType { get; set; } = null!;
        public int ActiveBranchCount { get; set; }
        public int CourtCount { get; set; }
    }
}
