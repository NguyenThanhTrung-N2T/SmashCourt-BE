namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — dùng trong repo, không expose ra ngoài API
    /// </summary>
    public class CourtTypeWithCount
    {
        public Entities.CourtType CourtType { get; set; } = null!;
        public int ActiveBranchCount { get; set; }
        public int CourtCount { get; set; }
    }
}
