namespace SmashCourt_BE.DTOs.Loyalty
{
    public class MyLoyaltyDto
    {
        public string TierName { get; set; } = null!;
        public int TotalPoints { get; set; }
        public decimal DiscountRate { get; set; }

        // Thông tin hạng tiếp theo
        // null nếu đang ở Diamond
        public string? NextTierName { get; set; }
        public int? PointsToNextTier { get; set; }
        public decimal? ProgressPercent { get; set; }

        public bool IsMaxTier { get; set; }
    }
}
